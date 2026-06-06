# Sprint 65 — Årsoversigt: Design Handoff Direction E replaces the Oversigt page

| Field | Value |
|-------|-------|
| **Sprint** | 65 |
| **Status** | complete |
| **Start Date** | 2026-06-05 |
| **End Date** | 2026-06-06 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes — `dotnet build` 0 errors at the close tree (38 solution warnings all pre-existing, none in S65 files); tsc clean |
| **Test Verified** | yes — unit 629 + regression **447/447 pristine + consecutive** (fresh-session exclusive run per FAIL-002) + smoke 5 + FE 173, all green locally; CI follows the held push (green S64 baseline 27009829974) |

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
| **External Codex** | cycle 1: 0 BLOCKER / 4 WARNING / 3 NOTE; cycle 2: 10/10 VERIFIED + 1 new WARNING (absorbed) — 2026-06-06 |
| **Internal Reviewer** | cycle 1: 0 BLOCKER / 4 WARNING / 5 NOTE; cycle 2: 10/10 VERIFIED + 1 new WARNING + 2 NOTEs (absorbed) — 2026-06-06 |
| **BLOCKERs resolved before Step 1** | ZERO BLOCKERs across both lenses, both cycles. All findings absorbed. **Step 0b CLOSED at cycle 2** (cap discipline; cycle-2 fixes are one-clause precision edits implementing the reviewers' own recommended wording — self-verified, rationale below) |

### Findings (cycle 1)

**External Codex** (all absorbed):
- **W1 (verified in code)**: `afholdt` filtering on `absences_projection.absence_type == category.type` would silently zero the Feriefridage row — projection rows carry `SPECIAL_HOLIDAY_ALLOWANCE`, the entitlement type is `SPECIAL_HOLIDAY` (`SkemaEndpoints.cs:59-72` `AbsenceToEntitlementType`, incl. 3 CHILD_SICK variants → CHILD_SICK). → TASK-6502 must aggregate via the shared mapping (promoted like `StandardDayHours` — no second copy); TASK-6504 gains a mapping-pin regression.
- **W2**: TASK-6504.4 `Σ afholdt == used` conflicts with future-dated planlagt rows appearing in `afholdt`. → invariant split three ways (past↔`used`, future↔`planned`, whole-year↔`used + planned`).
- **W3**: auth criteria missed the negative org-scope branch for non-employee actors. → leader/local-admin out-of-scope 403 regression added + same-route-employeeId gate language.
- **W4**: "all configs year-start dated" didn't pin WHICH OK version anchors each dated read. → explicit `OkVersionResolver.ResolveVersion(asOfDate)` anchor table + an OK24/OK26 straddle regression spanning the 2026-04-01 cutover.
- N1 transferable wording ("at boundaryMonth" readable as compute-at-December) → reworded compute-vs-emit. N2 DEP-004 endpoint-registry update unassigned → added to TASK-6506. N3 stale TASK-6506 CI-health wording (S64 now green) → updated.

**Internal Reviewer** (all absorbed):
- **W1**: pinned contract showed only a VACATION exemplar in `categories[]` — parallel FE task would infer the Feriefridage shape. → explicit SPECIAL_HOLIDAY/Feriefridage entry pinned (label confirmed at `BalanceEndpoints.cs:19` `DanishLabels`).
- **W2**: Feriefridage matrix-group-but-no-tile asymmetry is owner-intent but invites a "helpful" 7th tile. → explicit no-7th-tile sentence in the contract.
- **W3**: README "OK24" sub-line example vs contract "OK26" — README is pre-cutover illustrative (cutover 2026-04-01, `SkemaEndpoints.cs:316`); contract is right. → contract note added so nobody "fixes" it backward.
- **W4**: saldo (Sep–Dec incl. `carryoverIn`) and transferable (Dec) overlap by construction — owner-accepted (OQ-1). → TASK-6504 NOTE: intentionally non-additive, no saldo−transferable reconciliation test.
- NOTE (today seam): injection mechanism unpinned; no `IClock`/`TimeProvider` pattern exists in Backend.Api (raw `DateTime.UtcNow` idiom). → TASK-6502 now establishes `TimeProvider` (DI default `TimeProvider.System`) for the NEW endpoint only; TASK-6504 overrides with a fixed provider in the test host. Other NOTEs (extraction boundary sound, deletion claims verified sole-consumer, sickDaysYtd distinct-date justified, StandardDayHours promotion correct, KB refs all resolve) — confirmations, no action.

### Resolution
All 8 WARNINGs + 2 actionable NOTEs absorbed as plan edits 2026-06-06 (contract: SPECIAL_HOLIDAY entry, no-7th-tile, OK24-note, TimeProvider seam; TASK-6502: mapping reuse, OK-anchor table, compute-vs-emit wording, gate language; TASK-6504: invariant split, mapping pin, out-of-scope 403, OK straddle, non-additivity NOTE; TASK-6506: DEP-004 registry, CI-health wording). Cycle 2 (verification of these edits) below.

### Findings (cycle 2 — verification)

Both lenses: **all 10 cycle-1 absorptions VERIFIED; 0 BLOCKERs.** New findings, all absorbed:
- **Codex W (TASK-6504.4)**: the past/current-month vs future-month bucket split breaks if a future-dated absence sits inside the CURRENT month (it lands in the "current" bucket but is `planned`, not `used`). → split redefined by ABSENCE DATE (≤today ↔ used, >today ↔ planned); seed constraint added: no future-dated absences inside the current month.
- **Reviewer W (contract `transferable`)**: operand-dating of `carryoverIn/used/planned` was pinned only in TASK-6504.7, not in the contract the parallel Backend implementer builds against — wiring them to the LIVE current-ferieår row would be domain-wrong and fail test 7. → contract now pins them to the CLOSED boundary ferieår (same ferieår as `earnedAtBoundary`), explicitly contrasted with the live-balance `ferieRemaining` tile.
- **Reviewer NOTE**: OK-straddle test extended to also assert the `transferable` `carryoverMax` read anchors at the closed ferieår's (pre-cutover) entitlement-year start. **Reviewer NOTE (out-of-plan)**: `danish-agreements.md:117` still said §6 stk.2 "deferred to S64" — fixed to event-bound/S66 in this commit.
- Reviewer additionally confirmed TASK-6504.7's post-August-absence assertion **logically sound** (a Sep-Y absence books against ferieår Y → closed-Y−1 `transferable` unchanged, Dec saldo drops — correctly discriminates compute-at-model-boundary from compute-at-December).

### Resolution (cycle 2 / close)
Cycle-2 fixes are one-clause precision edits implementing the reviewers' own recommended wording (no new design decisions, no scope change). Per the 2-cycle-per-lens cap discipline (S63/S64 precedent), Step 0b CLOSES at cycle 2 with these edits self-verified; the implementation-time reviews (Step 5α/5a) provide the next independent check. Step 1 (decompose/dispatch) may begin.

## Pinned API Contract (TASK-6502 ⇄ TASK-6503/6504 interface)

`GET /api/balance/{employeeId}/year-overview?year=YYYY` — `EmployeeOrAbove` + employee-self / `OrgScopeValidator` (mirror `/summary` BalanceEndpoints.cs:56-67). Pure read; no events; no schema change.

```jsonc
{
  "employeeId": "emp001",
  "year": 2026,
  "today": "2026-06-05",            // server date — sole past/current/future + "Nu" authority. Derived ONCE per request from an injected System.TimeProvider (DI default TimeProvider.System; established by TASK-6502 for THIS endpoint only — tests override with a fixed provider in the WebApplicationFactory host)
  "header": {
    "employeeName": "Anna Berg",
    "agreementCode": "AC",           // dated read at today (user_agreement_codes)
    "okVersion": "OK26",             // OkVersionResolver at today — display context ONLY (matrix resolves per day). NB: the design README's "OK24" sub-line example is pre-cutover illustrative (cutover 2026-04-01) — do NOT "fix" this contract to match it
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
  // tiles are INTENTIONALLY the designed 6 — Feriefridage/SPECIAL_HOLIDAY is matrix-only (owner-ratified
  // "tiles stay the designed 6"); do NOT add a 7th tile.
  "months": [                        // index 0..11 = Jan..Dec of the selected year
    {
      "month": 1,
      "workedHours": 150.2,          // Σ work_time_projection rows in the month (ADR-028)
      "normHours": 147.9,            // Σ DailyNormCalculator per-day norms; null if ANY norm-bearing day resolves null (ANNUAL_ACTIVITY / no dated profile)
      "diff": 2.3                    // workedHours − normHours for months ≤ today's month; null for future months (no fabricated performance)
    }
  ],
  "categories": [                    // order: VACATION, SPECIAL_HOLIDAY, CARE_DAY, SENIOR_DAY — every entry below is REQUIRED; the FE renders all four plus the Arbejdstid group (5 matrix groups total)
    {
      "type": "VACATION",
      "label": "Ferie",              // labels from the DanishLabels map (BalanceEndpoints.cs:19): SPECIAL_HOLIDAY → "Feriefridage", CARE_DAY → "Omsorgsdage", SENIOR_DAY → "Seniordage"
      "saldo": [10.4, 12.5, ...],    // (number|null)[] — end-of-month remaining: EarnedToDate(annualQuota, 1.0m, entitlementYearStart, employmentStart, monthEnd) + carryoverIn − cumulative afholdt within the entitlement year containing that month (Jan–Aug = ferieår Y−1, Sep–Dec = ferieår Y for ResetMonth-9 types; calendar types = calendar year). Sep shows the reset sawtooth — correct domain shape. NULLABILITY (made explicit at Step-7a cycle 1): the graceful empty-config branch (no entitlement config under the employee's agreement/OK, e.g. academic AC_RESEARCH — ADR-023 D3) emits saldo as ALL-NULL with afholdt zeros + transferable 0; FE renders null as "–"
      "afholdt": [0, 1.5, ...],      // Σ absences_projection Hours MAPPED to this entitlement type in the month ÷ StandardDayHours (day-equivalents — same math as the quota guard SkemaEndpoints.cs:738/:1076; future-dated rows = planlagt). MAPPING IS MANDATORY via the shared AbsenceToEntitlementType map (SkemaEndpoints.cs:59-72): the SPECIAL_HOLIDAY row sums rows whose absence_type is SPECIAL_HOLIDAY_ALLOWANCE — filtering on the entitlement-type string directly silently zeroes that row (Step-0b Codex W1)
      "transferable": 5.0,           // min(max(0, earnedAtBoundary + carryoverIn − used − planned), carryoverMax); earnedAtBoundary COMPUTED at the type's model boundary (31 Aug of the selected year for ResetMonth-9 types — the ferieår spanning Sep Y−1–Aug Y; 31 Dec for calendar types); **carryoverIn/used/planned are the CLOSED-boundary-ferieår balances (the SAME ferieår as earnedAtBoundary — ferieår Y−1 for ResetMonth-9 types), NOT the live current-ferieår row** (Step-0b cycle-2 Reviewer W: the ferieRemaining tile above uses the live balances — different quantity); carryoverMax year-start dated (ADR-021 D2); deterministic derived boundary date
      "boundaryMonth": 12            // OQ-1 RESOLVED (owner 2026-06-06): 12 for ALL categories — DISPLAY anchor only. For ferie this is the legal §21 stk.2 transfer-agreement deadline (31 Dec of the calendar year whose ferieår ended 31 Aug); computation stays at the model boundary per `transferable` above
    },
    {
      "type": "SPECIAL_HOLIDAY",     // EXPLICIT so the parallel FE build doesn't infer (Step-0b Reviewer W1): same shape as VACATION
      "label": "Feriefridage",
      "saldo": [...],                // ResetMonth-9 entitlement year, same straddle as VACATION
      "afholdt": [...],              // sums SPECIAL_HOLIDAY_ALLOWANCE projection rows (mapping note above)
      "transferable": 0,             // carryoverMax 0 (danish-agreements.md:110) → always 0 → FE renders "–"
      "boundaryMonth": 12
    }
    // CARE_DAY ("Omsorgsdage") and SENIOR_DAY ("Seniordage") follow the same shape: calendar entitlement year,
    // carryoverMax 0 → transferable 0 → "–", boundaryMonth 12.
  ]
}
```

Notes: camelCase per existing endpoints; decimals as numbers (`Math.Round(., 2)` per the `/summary` idiom); dates ISO `yyyy-MM-dd`. Graceful per ADR-023 D3: profile-less/inconsistent employees get nulls/zeros, never a 500. Months before `employment_start_date` resolve no dated profile → `normHours: null` → "–" cells (mechanical, no special-casing).

## Task Log

### TASK-6501 — Sprint open: scaffold + ROADMAP re-prioritization + design handoff commit

| Field | Value |
|-------|-------|
| **ID** | TASK-6501 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Components** | docs/sprints, ROADMAP.md, design_handoff_oversigt/ |
| **KB Refs** | — |
| **Orchestrator Approved** | yes (scaffold+ROADMAP+handoff `13d6cc8`; entropy fix `2755b4c`; OQ-1 `75875a5`; Step-0b `50c9b95`) |

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
| **Status** | implemented — worktree branch merged `6be6d35` (3 batch commits `be25260`/`99ec53f`/`3903def`); post-merge validation green (build 0 errors, unit 629/629 unmodified) |
| **Agent** | Backend API (cross-domain authorized — S22/S24 convention; src/Backend/** only) |
| **Components** | Backend.Api (BalanceEndpoints, SkemaEndpoints, new Services/DailyNormCalculator.cs) |
| **KB Refs** | ADR-021 (D2 year-start dated config, D4 consumption-time lookup), ADR-023 D3 (graceful HTTP-read fallback), ADR-028 D3 (per-day norm semantics), ADR-030 D1–D6 (flat earned-to-date, employment_start_date), ADR-031 (fraction-independent day-count), ADR-003 (OK by entry date), ADR-007/ADR-009 (auth), DEP-004 (endpoint registry) |
| **Constraint Validator** | PASS (2026-06-06, 12/12 — std checks 1–8 + sprint-specific: single `7.4m` literal in EntitlementMapping.cs only; single AbsenceToEntitlementType definition w/ read-only alias in SkemaEndpoints; handler today solely via TimeProvider, DateTime.* only in comments; no SharedKernel/RuleEngine file touched) |
| **Reviewer Audit** | DONE (2026-06-06, MANDATORY): **0 BLOCKER / 0 WARNING / 2 NOTE — both pre-existing debt, explicitly verified behavior-preserved (not introduced by this diff).** All checklist items verified clean in code: DailyNormCalculator boundary clean (Services, per-call-local cache dict, no singleton mutable state); single 7.4m + single map w/ const/read-only aliases; ALL EarnedToDate calls flat 1.0m; GET /month refactor **semantically identical** (same cache key, per-day OK resolve, weekends 0, ANNUAL_ACTIVITY null, Round(weekly×fraction/5,2), decimal? shape); OK anchors all correct (per-day for norms, ferieår-start for configs, closedFerieaarStart for transferable config+carryoverMax, today only for display okVersion); **transferable operands traced to the CLOSED ferieår row (closedEntYear = year−1 for ResetMonth-9) — the Step-0b cycle-2 highest-risk wiring correctly implemented**; afholdt via EntitlementMapping.GetEntitlementType (W1 mapping honored, null-mapped rows filtered); straddle/sawtooth/cumulative-afholdt/future-diff-null/normHours-null all verified; auth mirrors /summary exactly (same route employeeId, no second id source); today once-per-request via TimeProvider; graceful paths null-guarded (no Single/First on empties). N1+N2 (pre-existing): employee_profiles-row-without-user_agreement_codes-row → S34 fail-loud EmployeeProfileNotFoundException → 500 in BOTH old GET /month and new endpoint's norm path; unseeded (agreement,OK) pair throws in ResolveAsync. → recorded as ROADMAP S65 follow-up (iv) — needs a design ruling (graceful ADR-023-D3 vs deliberate S34 fail-loud), no sprint-block |
| **Orchestrator Approved** | yes (2026-06-06 — CV PASS 12/12 + MANDATORY Reviewer audit 0B/0W; both NOTEs pre-existing → follow-up (iv); validation criteria all met) |

**Description**:
1. **Extract `DailyNormCalculator`** to `src/Backend/StatsTid.Backend.Api/Services/DailyNormCalculator.cs`: the TASK-5603 per-day norm loop verbatim from `SkemaEndpoints.cs:310-364` (dated profile per day via `IEmploymentProfileResolver`, `ConfigResolutionService` merged config with per-request cache key `okVersion|agreement|position|fraction|orgId`, `OkVersionResolver.ResolveVersion(day)` per day, weekends 0, `ANNUAL_ACTIVITY` → null, `Math.Round(weekly × fraction / 5, 2)`). Refactor SkemaEndpoints GET /month onto it **behavior-preservingly** (existing regression tests must stay green unmodified). It is an I/O-orchestrating read-side helper — it must NOT live in SharedKernel (ADR-002 boundary; refinement Reviewer W1).
2. **Promote `StandardDayHours = 7.4m`** (SkemaEndpoints.cs:75) AND the **`AbsenceToEntitlementType` map** (SkemaEndpoints.cs:59-72) to shared Backend.Api members consumed by both SkemaEndpoints and the new endpoint — no second literal, no second copy of the map (Step-0b Codex W1: `afholdt` aggregation MUST resolve projection `absence_type` through this map — e.g. `SPECIAL_HOLIDAY_ALLOWANCE` → `SPECIAL_HOLIDAY`, three `CHILD_SICK_DAY*` variants → `CHILD_SICK` — a direct entitlement-type filter silently zeroes the Feriefridage row).
3. **Implement `GET /api/balance/{employeeId}/year-overview?year=YYYY`** in BalanceEndpoints per the Pinned API Contract: auth mirror of `/summary` — the gate calls the **same `OrgScopeValidator` check on the same route `employeeId` that every downstream read uses** (no second id source; Step-0b Codex W3); per-month worked (work_time_projection range read) + norm (DailyNormCalculator) + diff (null future); categories VACATION/SPECIAL_HOLIDAY/CARE_DAY/SENIOR_DAY with saldo/afholdt day-equivalent arrays honoring the entitlement-year straddle; transferable **computed at the type's model boundary, emitted/displayed only at `boundaryMonth = 12`** (Option-B flat formula; Step-0b Codex N1 wording); tiles incl. sickDaysYtd (distinct SICK_DAY dates, current year) + eligibility flags (S59 eligibility read; birth_date + min_age for senior). All accrual via `AccrualMath.EarnedToDate(…, 1.0m, …)`; no duplicated rule logic anywhere.
4. **OK-version anchors (Step-0b Codex W4)** — every dated read uses `OkVersionResolver.ResolveVersion(asOfDate)` with the asOfDate matching the read's semantic anchor: per-day norm reads → that day (the DailyNormCalculator loop already does this); entitlement config reads (quota/carryoverMax/ResetMonth) → the entitlement-year START (ADR-021 D2, same as `/summary`); the transferable computation's config → the same entitlement-year start as its boundary; header okVersion → today (display only). Never today's OK for a historical-year read.
5. **Server `today` seam (Step-0b Reviewer NOTE)** — establish `System.TimeProvider` DI for THIS endpoint only (register `TimeProvider.System` as the default singleton; the handler derives `today = DateOnly.FromDateTime(provider.GetUtcNow().UtcDateTime)` ONCE per request). No other endpoint is refactored onto it this sprint. TASK-6504's test host overrides it with a fixed provider.

**Validation Criteria**:
- [ ] `dotnet build` 0 warnings 0 errors; existing Skema regression tests green unmodified
- [ ] Endpoint returns the pinned contract shape; 403 for foreign employeeId under Employee role AND for out-of-scope leader/local-admin (same OrgScopeValidator branch as `/summary`)
- [ ] No SharedKernel/RuleEngine file touched; no new literal `7.4`; no second `AbsenceToEntitlementType` copy; no `DateTime.Now`/`Today`/`UtcNow` in the new handler (server `today` solely via the injected `TimeProvider` seam)
- [ ] Graceful (never 500) for profile-less employees per ADR-023 D3

---

### TASK-6503 — ArsoversigtPage + route swap + SkemaPage params + deletions + FE tests

| Field | Value |
|-------|-------|
| **ID** | TASK-6503 |
| **Status** | implemented — worktree branch merged `058d796` (4 batch commits `fc4b2b3`/`f1c299b`/`08ba9b5`/`5d054de`); post-merge validation green (tsc clean, FE vitest 166/166 = 164 − 17 removed + 19 added) |
| **Agent** | UX Agent (frontend/**) |
| **Components** | frontend pages/hooks/routing |
| **KB Refs** | ADR-011 (design system: tokens, CSS Modules, AA), DEP-004; docs/FRONTEND.md; design_handoff_oversigt/README.md + reference/oversigt.{jsx,css} (Direction E only) |
| **Constraint Validator** | PASS (2026-06-06, 8/8 — scope frontend/** only; zero new hex in added lines; no kit.css/colors_and_type.css import; ComplianceWarnings/useBalanceSummary/useCompliance intact; deleted modules have zero remaining importers; exactly 6 tiles in buildTiles(), no 7th; month classification exclusively via server `today` (parseToday) — the one `new Date().getFullYear()` is the year-switcher useState seed only, NOTE not violation) |
| **Reviewer Audit** | DONE (2026-06-06, focused): **0 BLOCKER / 1 WARNING / 2 NOTE.** All P1/P2 items confirmed clean in code — server-today authority airtight incl. loading/error/stale-data paths (no client-clock fallback anywhere; year-switch renders OLD data against OLD server-today until new data arrives); contract fidelity (4 categories via map + Arbejdstid = 5 groups; transferable only at boundaryMonth >0; 6 tiles; da-DK); hook = useBalanceSummary shape; deletions verified zero live importers (only a comment mention in ComplianceWarnings.tsx:9); a11y (th scope col/row, focus-visible tokens, aria-labels). W (test quality): year-switcher test asserted only `typeof === number`, not the decrement — would pass on `year+1`. N1: dead `nowIndex` prop on CategoryGroupProps. N2 = W restated. **W+N1 ABSORBED in this commit** (Small Tasks Exception, Orchestrator self-checked vs CV list): test now captures seed year from the initial hook call and asserts ←=seedYear−1 then →=seedYear; dead prop dropped from interface+call site. Post-fix: tsc clean, FE 166/166 |
| **Orchestrator Approved** | yes (2026-06-06 — CV PASS + Reviewer findings absorbed; validation criteria all met) |

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
| **Status** | complete — cycle-1 `6571156` (18 tests, Sonnet during the 2026-06-06 Opus 529 outage) + Step-5a fix-forward `f2b2ef6` (Opus; ALL 3 B + 3 W fixed, +2 tests) merged `9b6db67`; **20/20 pristine + consecutive on the branch, 20/20 re-verified on master post-merge**; build 0 errors, −1 warning (replica deletion) |
| **Agent** | Test & QA Agent (tests/**) |
| **Components** | tests/StatsTid.Tests.Regression (Docker-gated; services-postgres in CI per S64) |
| **KB Refs** | ADR-021 D2, ADR-028, ADR-030, ADR-031; S64 census conventions (`RegressionSeed.SeedEmployeeAsync`, sequential runner, citation-gated assertions) |
| **Constraint Validator** | PASS (2026-06-06, 9/9 — tests/** only, single new file; xunit.runner.json untouched; no shared-fixture edits; zero wall-clock reads (FixedTimeProvider, today=2026-06-15); citation comments outside all 7 SQL raw strings; no assertion-weakening patterns; conventions match BalanceSeriesTests) |
| **Reviewer Audit** | cycle 1 DONE (2026-06-06, Opus): **3 BLOCKER / 3 WARNING / 4 NOTE** with full 11-area coverage matrix — areas 1/3/6/9/10/11 solidly pinned (straddle test called "strongest in the suite"); areas 2/4/7/8 under-assert. **B1** byte-equality test compares ONE decimal, not raw bodies; **B2** transferable formula never asserted with non-trivial operands (VACATION cap=5 available; suite would pass if `carryoverIn−used−planned` were dropped); **B3** OK-straddle discriminates nothing (CentralAgreementConfigs AC/OK26 is a value-identical placeholder; test asserts only >0 — the pre-flagged failure mode). **W1** marquee Skema reconciliation asserts the endpoint-internal tautology `diff==worked−norm`, never calls GET /month; **W2** consumption split asserts monthly-totals proxy, not used/planned; **W3** test-local AccrualMath replica structurally diverges from production for the employment-start branch (latent — all current seeds have null employment_start). N: mapping literal accepted (internal-class accommodation); seam/seed-hygiene/cap-0 confirmations. → ALL B + W re-dispatched 2026-06-06 (same worktree branch, Opus); NOTE A accepted as-is. **Fix-forward `f2b2ef6` (Orchestrator-verified per the self-review constraint):** B1 → raw-body `ReadAsStringAsync` exact equality ×2 fetches; B2 → two new Facts pin BOTH min() branches with exact values off the real `AccrualMath` at the closed boundary (below-cap: used=22 → raw 3 < cap → **3**; at-cap: raw 25 → **5**; the weak ≥0 tightened to ==5; closedEntYear=Y−1 row-key verified against the handler); B3 → **ADR-017 local-profile route**: two `local_agreement_profiles` rows (OK24 weekly 30 / OK26 weekly 35) on a dedicated `STY_OKDISC` org (zero shared-row contamination) → exact distinct per-side norms (6.00 vs 7.00/weekday) + per-weekday-rate cross-check that fails if OK were resolved once per year; W1 → genuine cross-seam: GET /api/skema/{id}/month reconstructed month-diff == year-overview `months[1].diff`; W2 → seeded-balance totals-consistency proxy with explicit in-test comment (real /save flow disproportionate — Orchestrator concurs); W3 → replica deleted, ALL expected values via real `AccrualMath.EarnedToDate` + the endpoint's outer rounding (values unchanged for live cases — replica was right where exercised). **Declared residual gap (accepted):** carryoverMax cannot be made OK-discriminating without mutating the global `entitlement_configs` row (AC OK24≡OK26 placeholders, both cap 5; local profiles steer only weekly_norm) — documented in-test; the value+anchor are pinned, the per-day NORM discrimination is exact. Self-resolves when real OK26 configs diverge |
| **Orchestrator Approved** | yes (2026-06-06 — CV PASS 9/9 on cycle 1 + diff self-check on the fix; Reviewer cycle-1 findings ALL closed; merged `9b6db67`, 20/20 on master) |

**Description**: New Docker-gated regression class(es) for the year-overview endpoint:
1. **Auth**: employee self 200; employee→foreign 403; leader-with-scope 200; **leader/local-admin OUT-of-scope → 403** (the negative org-scope branch, Step-0b Codex W3; mirror BalanceSeriesTests auth shape).
2. **Skema reconciliation (marquee)**: seeded month with work-time rows → `months[m].diff` equals the sum of Skema GET /month per-day diffs for the same month (the shared-helper drift-proof, asserted end-to-end).
3. **Day-equivalents**: a 3,7 t (half-day) VACATION absence → that month's `afholdt = 0,5` and saldo drops 0,5 — not 1 (cites SkemaEndpoints.cs:738 semantics).
4. **Consumption-reconciliation (split per Step-0b Codex W2; date-based per cycle-2 Codex W)**: the underlying split is by ABSENCE DATE, not month bucket — absence dates ≤ today ↔ `used`, dates > today ↔ `planned`, whole entitlement year ↔ `used + planned`. Because the API emits monthly totals, the regression seed MUST NOT place future-dated absences inside the current month (a same-month future row would land in the "current" bucket and falsely fail `≤today ↔ used`); seed past months + future months only, and assert (a) Σ past+current-month `afholdt` == `used`, (b) Σ future-month `afholdt` == `planned`, (c) whole-year Σ == `used + planned`.
5. **Absence-type mapping pin (Step-0b Codex W1)**: a seeded `SPECIAL_HOLIDAY_ALLOWANCE` absence appears in the SPECIAL_HOLIDAY/"Feriefridage" row's `afholdt` (and a `CHILD_SICK_DAY_2` row would map to CHILD_SICK had it a category — assert via the shared map, not a re-derived literal).
6. **Straddle**: absences in March (ferieår Y−1) and October (ferieår Y) count against their own ferieår; Sep saldo shows the reset; carryoverIn case included.
7. **Transferable determinism**: two identical requests byte-equal; formula = min(max(0, earnedAtBoundary + carryoverIn − used − planned), year-start carryoverMax); cap-0 type → 0 (rendered "–"); value emitted only at `boundaryMonth = 12`; computed at the model boundary (NOT December) — assert via a seeded post-August absence that does NOT change ferie `transferable` but DOES change Dec `saldo`.
8. **OK-version straddle (Step-0b Codex W4)**: a year spanning the 2026-04-01 OK24→OK26 cutover resolves per-day norms with the correct version on each side (and entitlement config reads anchor at the entitlement-year start, not today). Extended per cycle-2 Reviewer NOTE: also assert the `transferable` `carryoverMax` config read anchors at the (pre-cutover) entitlement-year start of the CLOSED boundary ferieår — not today's OK.
9. **Future months**: `diff: null` after the (controlled) today; planned future absence appears in `afholdt`.
10. **ANNUAL_ACTIVITY**: academic profile → `normHours: null` months.
11. **Graceful**: profile-less employee → 200 with nulls (ADR-023 D3), never 500.
NOTE: today-dependent assertions drive the TASK-6502 `TimeProvider` seam (fixed provider in the test host — no wall-clock-dependent expected values; the S31/S34 `-infinity` sentinel lesson applies to any DateOnly.MinValue SQL literals).
NOTE (Step-0b Reviewer W4): `saldo` (Sep–Dec includes `carryoverIn`) and `transferable` (displayed Dec) overlap BY DESIGN (owner-accepted, OQ-1) — do NOT write a saldo−transferable reconciliation assertion; it would fail by design.

**Validation Criteria**:
- [ ] Suite green pristine + consecutive (S64 discipline); no `src/` changes required by tests (else declare cross-domain, never self-fix)
- [ ] Census-style citation comments OUTSIDE SQL raw-string literals (S64 lesson)

---

### TASK-6505 — ADR-030 annotation: the transferable display projection

| Field | Value |
|-------|-------|
| **ID** | TASK-6505 |
| **Status** | complete (done early, during Phase-1 agent runtime — docs-only, no file overlap) |
| **Agent** | Orchestrator (docs/ is Orchestrator-only) |
| **Components** | docs/knowledge-base/decisions/ADR-030, docs/knowledge-base/INDEX.md |
| **KB Refs** | ADR-030, ADR-031 |
| **Orchestrator Approved** | yes — **D9** added (definition/formula/as-of/display-anchor/non-equivalence/model-vs-law gap, research-cited); courtesy ⚠-SUPERSEDED marker on D8 (read as live before); INDEX rows updated (ADR-030 +D9; ADR-031 stale S64 refs → event-bound/S66) |

**Description**: Carried-over obligation from the parked 2026-06-03 refinement: annotate ADR-030 with the transferable figure's definition, formula, as-of policy (boundary projection, "max if you book nothing more"), and explicit **non-equivalence** to the deferred §21/§26 settlement. Include the OQ-1 research verdict (RESOLVED 2026-06-06): legal transfer point = 31 Dec per Ferielov §21 stk.2 (display anchor `boundaryMonth = 12`); the model's Sep-rollover carryover is an accrual-side approximation of the legal transfer event; absent a §21 agreement by 31 Dec the untaken 5th week is auto-paid by 31 March (out of scope, settlement). Also record the særlige-feriedage model-vs-law gap (calendar-year accrual, 1 May–30 Apr taking, 2½% godtgørelse payout per Cirkulære 021-24 §12 stk.2 — vs the system's ResetMonth-9/carryover-0 simplification). Cite `docs/references/ferie-transfer-timing-research.md` (committed this sprint).

---

### TASK-6506 — Validation, Step 7a, close

| Field | Value |
|-------|-------|
| **ID** | TASK-6506 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Components** | — |
| **Orchestrator Approved** | yes — sprint-test-validation delta table (+32 → 1254 total); Step 7a dual-lens ×4 cycles (artifacts `.claude/reviews/SPRINT-65-step7a-{codex,reviewer}.md`); DEP-004 Balance section + /series FE-orphaned + registry-drift note (`c08530e`); KB: PAT-007 + PAT-008 + FAIL-002 + ADR-030 D9 (46 entries); INDEX/QUALITY anchors → 65; ROADMAP follow-ups (iv)+(v) recorded; close gates: CI-health green baseline 27009829974 (nothing pushed since), Step-7a artifacts with verdict + reviewed-against-commit lines, close commit from repo root |

**Description**: `dotnet build` + full pyramid (sprint-test-validation skill: unit + regression pristine/consecutive + smoke + FE with delta arithmetic vs S64's 629/424/5/164); Constraint Validator per task; Step 5a Reviewer audits; Step 7a dual-lens sprint-end review; **DEP-004 endpoint-registry update (Step-0b Codex N2)**: register `GET /api/balance/{employeeId}/year-overview` + `useYearOverview` + `ArsoversigtPage`, remove the deleted OversightPage/LeaveOverview/AccrualTrend/useAccrualSeries rows, mark `GET /api/balance/{id}/series` FE-orphaned/retained; close gates (Step-7a artifacts + CI-health — gate operates against the GREEN S64 baseline, run `27009829974`; any red this sprint introduces is S65's to fix forward + Test-Verified line + repo-root CWD); ROADMAP Completed-Sprints row + INDEX update (INDEX freshness warning already >3 behind — refresh anchors); commit + push.

## Phases & Dependencies

- **Phase 1 (parallel, worktree isolation)**: TASK-6502 (Backend) ∥ TASK-6503 (UX — builds against the pinned contract with mocked fetch)
- **Phase 2**: TASK-6504 (Test & QA — needs TASK-6502 merged)
- **Phase 3**: TASK-6505 + TASK-6506 (Orchestrator)

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified (display) | OQ-1 verdict recorded: legal ferie transfer point = 31 Dec (Ferielov §21 stk.2; state sector via Cirkulære 021-24 §3) → `boundaryMonth = 12`; the displayed figure is the model's Sep-rollover projection, NOT the legal residual-5th-week quantity (non-equivalence annotated in ADR-030 per TASK-6505; §21/§26 settlement stays deferred). Særlige-feriedage calendar-year/1 May–30 Apr gap (Cirkulære 021-24 §12 stk.2) recorded as ROADMAP follow-up. Flat accrual per ADR-031 reused unchanged. **Step-7a hardening: historical ferieår now valued under the agreement in force at each ferieår start** (C1/C2-1/C3-1 chain — was today's agreement). Full citations: `docs/references/ferie-transfer-timing-research.md` |
| Wage type mappings produce correct SLS codes | N/A | read-only sprint; no payroll path touched |
| Overtime/supplement calculations are deterministic | verified | byte-equal raw-body determinism test (two identical requests) + fixed-TimeProvider seam — the response is a pure function of (employeeId, year, today, projections, configs); transferable computed at the model boundary, proven insensitive to post-August absences (regression 7) |
| Absence effects on norm/flex/pension are correct | verified | day-equivalent afholdt (3.7 t → 0.5) + date-based used/planned split + SPECIAL_HOLIDAY_ALLOWANCE mapping pin + ferieår straddle/sawtooth/carryoverIn (regression 3–6); norm path byte-preserved vs the Skema seam (cross-seam reconciliation, regression 2) |
| Retroactive recalculation produces stable results | N/A | no recalculation surface |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Form** | Codex: `codex review --base 0fcf998` (base-anchored — intermediate commits exist; steering prompt unavailable in this form per AGENTS.md). Internal Reviewer: Opus agent, sprint-end lens, full `0fcf998..4401d40` diff |
| **Cycle 1 — Codex** | 2 P1 + 1 P2 (2026-06-06): **C1 (P1→BLOCKER)** historical entitlement years valued with TODAY's agreement code (BalanceEndpoints.cs:716/:753 — OK version year-start-anchored, agreement operand not; employee switching AC→HK sees old AC ferieår under HK quotas); **C2 (P1→BLOCKER)** null-saldo render crash (graceful branch emits saldo all-null; FE `v !== 0` branch calls `formatDanishNumber(null)` → `null.toFixed` TypeError → page down); **C3 (P2→WARNING)** useYearOverview stale year-switch response overwrites newer data |
| **Cycle 1 — Internal Reviewer** | 1 WARNING + 2 NOTE (2026-06-06): **R-W1 ≡ C2 (CONVERGENT)** — adds the reachability proof: AC_RESEARCH/AC_TEACHING have no seeded entitlement configs and `users.ok_version` is unconstrained TEXT, so an academic employee (a persona the regression suite itself seeds at YearOverviewTests.cs:923) returns all-null saldo for ALL FOUR categories → guaranteed crash; invisible to every per-task lens (backend review verified the null-emit as intentionally graceful, FE tests mocked numeric saldo only, the academic regression test reads only months[].normHours). **R-N1** formatDanishNumber 1-dp matrix display of 2-dp values (14.08→"14,1") — ACCEPTED as intended matrix precision (consistent with tiles; "0,5" exact). **R-N2** TASK-6506 close items pending in the reviewed diff — expected state; everything else cross-checks (contract field-for-field FE↔endpoint↔tests; read-only surface confirmed — no events/outbox/audit writes; deletions fully unwired; D9↔implementation consistent; TimeProvider new-endpoint-only; SharedKernel/RuleEngine untouched) |
| **Absorption (cycle 1)** | C1 → Backend fix agent (per-ferieår dated agreement resolve, cached, fallback chain agreement-aware); C2/R-W1 + C3 → UX fix agent (saldo `(number\|null)[]` + dash guard + crash test; stale-response guard + race test); contract nullability made explicit (this commit). Lens complementarity held again: the agreement-anchoring P1 was Codex-only, the reachability depth was Reviewer-only |
| **Cycle 2 — Codex** | cycle-1 fixes verified silently (no re-raise); **2 NEW P1 (2026-06-06)**, same agreement-anchoring family, deeper: **C2-1 (P1→BLOCKER)** the categories graceful-empty branch keys off TODAY's agreement having a config — a today-configless employee loses ALL historical years (the early `continue` precedes the C1 per-ferieår resolution); **C2-2 (P1)** tiles read the current entitlement-year config with today's agreement (an AC→HK mid-year switch revalues the same balance row under HK rules) |
| **Cycle 2 — Internal Reviewer** | **"All cycle-1 absorptions verified. No new findings."** Verification depth: C1 byte-identity induction for single-agreement employees + never-null fallback chain + no-residual-leak sweep (explicitly judged the remaining todayAgreementCode reads — SENIOR_DAY min-age, tiles CurrentRemainingAsync — "anchored at today's entitlement year — correct, not historical"); C2/C3 no-stuck-loading proof (latest request always clears its own loading; stale responses touch nothing) + null-guard single-consumption-site proof + genuine pre-fix-fails pins; pins arithmetic re-derived against real AccrualMath + dated-predicate + OK cutoffs; PIN codes contamination-free (single-file, additive, idempotent) |
| **Cycle-2 adjudication** | **C2-1 → FIX in-sprint** (same family as C1, new-handler-only, no cross-endpoint coupling) — Backend fix agent dispatched (year-start-agreement attempt before the empty branch; PIN-2 path provably unchanged). **C2-2 → ACCEPTED-BY-DESIGN, no fix**: the tiles MIRROR /summary per the pinned contract ("current entitlement-loop remaining"), and /summary's live-agreement read is the system-wide convention (ADR-023 D2 — the documented Phase-4e LAUNCH-BLOCKING determinism item); forking the year-overview tile from /summary would show one employee two different Ferie-remaining figures on two pages. The two lenses split exactly on priors here (Codex w/o priors flags it; the internal Reviewer w/ priors independently judged the same reads correct) — Orchestrator rules with the internal lens + the contract. Recorded against the Phase-4e item |
| **Cycle 3 — Codex** | C2-1 fix VERIFIED (no P1 re-raise); **3 NEW P2→WARNING (0 BLOCKERs — the cap-discipline halt does NOT fire;** severity declining per cycle: 2P1+1P2 → 2P1 → 3P2): **C3-1** the C2-1 Jan-1 probe misses a Sep-reset ferieår begun under a configured agreement (Oct-switch edge — Jan–Aug wrongly empty); **C3-2** `today` is the UTC date — Copenhagen is the next calendar date between local and UTC midnight (the "Nu"/future boundary + dated reads misclassify in that 1–2h window); **C3-3** failed year-switch keeps the old matrix silently (page suppresses error when data exists) while drill-ins navigate with the NEW year-state → mismatched drill-down |
| **Cycle-3 adjudication (Orchestrator discretion — WARNINGs)** | **C3-1 → FIX** (mechanical completion of the C2-1 family: ordered candidate-anchor probe {Jan-1 Y, Sep-1 Y−1, Sep-1 Y} through the existing cached helpers; pin personas provably unaffected). **C3-2 → ACCEPTED + ROADMAP follow-up (v)**: every endpoint shares the system-wide UTC-date idiom (`DateTime.UtcNow`-derived dates) — fixing one endpoint forks date semantics; needs a system-wide business-timezone ADR decision. **C3-3 → FIX** (FE: drill-in anchored to `data.year` — the year actually displayed — + visible stale-data error banner naming both years). Both fixes get a scoped cycle-4 verification per the post-Step-7a coverage rule |
| **Cycle 4 (scoped: C3-1 + C3-3 fixes)** | pending |

## Test Summary

**Test Verified**: yes — full pyramid green LOCALLY at the close tree (`aad71dd` + tests built `aad71dd`-tree): unit 629/629; **regression 447/447 PRISTINE + 447/447 CONSECUTIVE** (twice back-to-back, S64 discipline, on a FRESH Docker Desktop session with an exclusive run per FAIL-002); smoke 5/5; FE vitest 173/173 (25 files). CI verification follows the push (held for owner go; baseline = green S64 run 27009829974).

## Test Validation Report
| Suite | Previous | Current | Delta |
|-------|----------|---------|-------|
| Unit | 629 | 629 | +0 |
| Regression | 424 | 447 | +23 |
| Smoke | 5 | 5 | +0 |
| Frontend | 164 | 173 | +9 |
| **Total** | **1222** | **1254** | **+32** |

Delta arithmetic: regression +23 = +20 TASK-6504 suite (incl. the Step-5a fix-forward's +2 transferable cases) +2 Step-7a pins (dated-agreement valuation; academic graceful categories) +1 cycle-2 pin (configless-today historical valuation). FE +9 = −17 (deleted with the Oversigt surface) +19 (ArsoversigtPage 15 + SkemaPageParamInit 4) +3 (Step-7a C2/C3: all-null saldo render + hook race ×2) +3 (C3-3: drill-in/banner) +1 (C4-1 out-of-range-year). Smoke unchanged (5 incl. the S64 deny-pin).

**Environmental-flake dossier (FAIL-002, discovered this close)**: three non-final runs on a long-lived Docker session showed `DockerApiException`-at-container-start failures (23 → 1 → 1, always `[1 ms]` class-init, different classes each time — incl. one run contended by a parallel agent's class executions); the same trees ran 444/444 and the final 447/447×2 green on quiet fresh sessions. All failures matched the FAIL-002 signature (infrastructure, zero assertion failures); no test was modified in response. Full per-run logs captured via Out-File (close-protocol rule).

## Agent Effectiveness

| Agent (wave) | Outcome |
|---|---|
| TASK-6502 Backend (overnight, Opus) | first-pass clean — all sharp edges (closed-ferieår operands, mapping, behavior preservation) verified held by CV + MANDATORY Reviewer (0B/0W) |
| TASK-6503 UX (overnight, Opus) | first-pass clean — 1 W + 1 N absorbed (year-switcher test pin, dead prop); proposed PAT-007 (approved) |
| TASK-6504 Test & QA (Sonnet, during the Opus 529 outage) | green-but-under-asserting — Step-5a Opus Reviewer found 3 BLOCKERs of under-assertion (byte-equality on one field; trivial-operand transferable; non-discriminating OK straddle) → Opus fix-forward `f2b2ef6` strengthened all; proposed PAT-008 (approved). Lesson: verification work degraded gracefully on the smaller model, generative test-authoring did not — see [[opus-unavailable-notify-owner]] handling rule |
| Step-7a fix agents ×4 (Opus) + pins ×2 | all first-pass clean with explicit byte-identity arguments; 1 salvage (the C2-1 pin agent ended mid-wait misattributing a foreign testhost as its own second pass — work salvaged from its worktree per the S22 lesson, committed by the Orchestrator) |
| Constraint Validators ×3 (Sonnet) | PASS 12/12, 8/8, 9/9 — mechanical checklist work fully Sonnet-viable |
| Reviewers (Step-5a ×2 Sonnet-during-outage + 6504 Opus; Step-7a internal ×3 Opus) | the Opus 6504 review caught what two Sonnet-lens passes structurally could not (under-assertion); the Step-7a internal cycle-2/4 verification proofs (byte-identity induction, no-stuck-loading) were load-bearing for the fix approvals |

## Sprint Retrospective

- **The dual-lens cap discipline earned its cost**: 4 Codex cycles at monotonically declining severity (2P1+1P2 → 2P1 → 3P2 → 1P2), every fix verified, two principled acceptances recorded with both-lens reasoning (C2-2 tiles-mirror-/summary; C3-2 UTC-date convention → ROADMAP (v)). The agreement-anchoring P1 family (C1→C2-1→C3-1) was invisible to every project-priors lens — external Codex caught all three iterations; the internal Reviewer supplied the proofs that made the fixes safely mergeable. `review-lens-complementarity` confirmed again.
- **Opus 529 outage (~70 min)**: all subagent spawns died server-side; Sonnet carried the audit layer (flawlessly) and one generative task (TASK-6504 — which the Opus reviewer then had to strengthen). New standing rule [[opus-unavailable-notify-owner]]: notify + hold generative work by default.
- **FAIL-002 discovered + KB'd**: Docker Desktop sheds testcontainer starts under sustained churn (3 observations: 23/1/1 failures, always DockerApiException at [1 ms] init, never on a quiet fresh session). Close protocol now: fresh Docker session + exclusive runs + full-log capture.
- **Process learnings**: tail-only log capture cost a 20-min re-run (always Out-File close runs); a background agent that backgrounds its own validation can end mid-wait — salvage from the worktree, don't re-dispatch; CWD drifts between PowerShell tool calls (always `Set-Location` before git); PS 5.1 mangles embedded double quotes in here-string args to native commands.
- **Scope honesty**: the pinned contract gained explicit `saldo` nullability only at Step 7a — the graceful branch had emitted nulls from day one while the FE typed `number[]`; cross-layer nullability is now a standing thing to pin in contracts up-front.
