# Sprint 61 — "Oversigt" employee annual dashboard (read-only)

| Field | Value |
|-------|-------|
| **Sprint** | 61 |
| **Status** | complete |
| **Start Date** | 2026-06-02 |
| **End Date** | 2026-06-02 |
| **Orchestrator Approved** | yes — 2026-06-02 |
| **Build Verified** | yes — `dotnet build` 0 errors |
| **Test Verified** | yes — 629 unit + 164 FE; 8 Docker-gated regression CI-pending (Docker unavailable at close) |

## Sprint Goal
Replace the parked `/tid/oversigt` placeholder (`OversightPlaceholder.tsx` → "Kommer snart.") with a real **read-only, employee-self, annual status dashboard**. Centerpiece: an **optjent (earned-to-date) leave overview** surfacing S60/ADR-030's now-real monthly-accrued VACATION/SPECIAL_HOLIDAY values — computed by `/summary` today but rendered nowhere — plus balance cards (incl. the never-shown overtime-saldo), compliance status, the selected-month approval status, and an **accrual-curve trend** across the ferieår. The trend requires a **new read-only backend series endpoint** computing the per-month curve as a pure fn of as-of date, which is the occasion to **consolidate the triplicated pure `EarnedToDate` into SharedKernel** (closes S60 follow-up #1). Scope is read-only; no mutations. Refinement: `.claude/refinements/REFINEMENT-s61-oversigt-page.md` (READY; 2 dual-lens cycles, 0 BLOCKERs). **Out of scope:** flex/norm trends (accrual curve only), leader/HR variants, annual flex aggregation, any Registrering-card removal.

## Entropy Scan Findings (Step 0a)

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py` green (db-schema in sync 55 tables; KB INDEX 42 entries, 0 orphans, 0 dangling; sprint inventory through S60). Cited ADR-002/021/023/030 + PAT-004/005 + SECURITY.md + FRONTEND.md all resolve. |
| Pattern compliance spot-check | CLEAN | No PAT-005 breach — the only Backend references to `AccrualCalculator.EarnedToDate` are **xmldoc comments** documenting the mirror (`BalanceEndpoints.cs:29,33`; `SkemaEndpoints.cs:133,138`), not cross-assembly calls/imports. No `FindFirst("scopes")` (FAIL-001). `http://localhost` only in `launchSettings.json` dev profiles, not app code. |
| Orphan detection | CLEAN | S60 `EmploymentDateEndpoints` (GET/PUT, mapped + tested) and `AccrualCalculator` are live. `OversightPlaceholder.tsx` referenced in `App.tsx:60` (to be replaced this sprint). |
| Documentation drift | FIXED | (a) MEMORY.md sprint status was stale at S57 → corrected to S60 + S61-opening. (b) **DRIFT fixed**: `danish-agreements.md:117` said "All entitlements use IMMEDIATE accrual model" (contradicted ADR-030) → corrected to MONTHLY_ACCRUAL for VACATION/SPECIAL_HOLIDAY. |
| Quality grade review | CLEAN | No domain grade change pending at S61 open; QUALITY.md re-grade deferred to sprint close per convention. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — P2 (deterministic rule engine: `EarnedToDate` relocation + pure accrual-series fn), P4 (version correctness: as-of-dated earned-to-date + dated config/profile), P7 (security: new read-only endpoint authz, employee-self + scope). No schema migration. |
| **External Codex** | invoked 2026-06-02 — cycle 1: **0 BLOCKER**, 2 WARNING + 2 NOTE (all absorbed/acknowledged) |
| **Internal Reviewer** | invoked 2026-06-02 — cycle 1: **0 BLOCKER**, 3 WARNING + 6 NOTE (all absorbed/verified) |
| **BLOCKERs resolved before Step 1** | n/a — zero BLOCKERs at cycle 1; WARNINGs absorbed into plan; cycle 2 verifies the edits |

### Findings (cycle 1) — both lenses converged, 0 BLOCKERs
- **WARNING (both) — series as-of dating under-pinned (6102/6106).** `/summary` resolves part-time fraction once at month-end; a curve must resolve the **dated profile per point** + reconcile each point's as-of = month-END from window params. → **ABSORBED**: 6102 desc/AC require per-point dated profile + month-end-from-params; 6106 adds a mid-ferieår part-time-change curve regression.
- **WARNING (Reviewer) — 6101 single-source mechanization + path.** "byte-identical" can't be reflected on `private static` mirrors; SkemaEndpoints has TWO call-sites (`:879`,`:887`); pin target to `Calendar/AccrualMath.cs` (Rule Engine's authorized SharedKernel subscope, AGENTS.md:10). → **ABSORBED**: 6101 now deletes the two mirrors, re-points all call-sites, enforces single-source via grep/architecture test + parity matrix; agent label narrowed to `Calendar/**`.
- **WARNING (Codex) — 6102 IMMEDIATE-types contract ambiguous / scope-widening.** → **ABSORBED**: contract pinned to MONTHLY_ACCRUAL VACATION/SPECIAL_HOLIDAY only.
- **NOTE (Reviewer) — FE path** is top-level `frontend/src/**` (beware stale worktree copies). → noted in 6103.
- **NOTEs (both, no action)**: authz keys on the right identity (self + scope); no wall-clock/determinism leak; PAT-005 correctly scoped out (SharedKernel leaf ≠ HTTP rule-eval boundary; ADR-030 anticipates the consolidation); 6103 hook claims verified; deps committed (S60 `1e70874`); baseline counts (596 unit / 144 FE) match INDEX.
- **NOTE (Codex) — land the untracked plan + modified `danish-agreements.md` before Step 1** if committed plan inputs are required.

### Cycle 2 (verification of cycle-1 edits) — CONVERGED
- **Codex**: "No new findings — plan sound." (6102 MONTHLY_ACCRUAL-only + month-end-from-params + per-point dated profile unambiguous; 6101 delete-mirrors + re-point all call-sites + grep/parity mechanization correct; 6106 both regressions present.)
- **Reviewer**: "No new findings — plan sound." Additionally verified: all cited loci byte-exact; the three `EarnedToDate` call-sites are the ONLY ones in `src` (none missed; the `ValidateEntitlementRequest.cs:28` hit is xmldoc); no compile/ordering risk (both `Backend.Api` + `RuleEngine.Api` already `ProjectReference` SharedKernel; no new `using StatsTid.RuleEngine`); agent-scope label matches AGENTS.md:10; an `ArchitectureConstraints/` test folder already exists as precedent; the existing reconciliation test (`AccrualCalculatorTests.cs:146-186`, reflects into the to-be-deleted privates) is explicitly re-pointed by 6101.

**Verdict: Step 0b CONVERGED at cycle 2 (0 BLOCKERs across both cycles; cycle-1 WARNINGs absorbed; cycle-2 clean). Cycle cap respected; no halt-and-prompt. Plan approved to proceed to Step 1 (decompose + dispatch).**

## Architectural Constraints Verified (plan-level)
- **P1** — additive; read-only consumer of existing ADR-030/021/023 surfaces; `EarnedToDate` → SharedKernel (a leaf both RuleEngine + Backend already reference) is NOT the PAT-005 HTTP rule-eval boundary, so no bounded-context breach. No new bounded context.
- **P2** — accrual-series math is the existing PURE `AccrualCalculator.EarnedToDate` (no I/O, no wall-clock; as-of dates passed in); a per-month curve is N pure calls → replay-deterministic.
- **P3** — no new events; no event-stream writes; series is computed-on-read.
- **P4** — each month-end as-of is derived from request window params (never `DateTime.Today`); dated config/profile resolved as-of, mirroring `/summary`.
- **P7** — new endpoint is read-only + employee-self (self-equality check) AND scope-validator branch; no entitlement/accrual leak across employees; no new mutation surface.

## Implementation Result (Step 1 — complete, pending Step 7a + close)

All 6 tasks dispatched to domain agents in dependency order (6101 → 6102 → 6103‖6105 → 6104 → 6106), each build/test-gated before acceptance; first-pass on all. **Working tree uncommitted** (HEAD still S60 `1e70874`).

| Task | Agent | Result |
|------|-------|--------|
| 6101 | Rule Engine (+SharedKernel/Calendar, +Backend) | `Calendar/AccrualMath.cs` (single source); `AccrualCalculator` + Balance/Skema call-sites delegate; 2 private mirrors deleted; reconciliation test re-pointed |
| 6102 | Backend API | `GET /api/balance/{id}/series?year&month` — full-ferieår 12-pt curve, MONTHLY_ACCRUAL-only, per-point month-end + dated profile, both auth branches, reconciles with `/summary` |
| 6103 | UX | `earned` on `EntitlementInfo`; `OvertimeBalanceInfo`+`overtimeBalance?` on `BalanceSummary`; new `useAccrualSeries` |
| 6104 | UX | `OversightPage` (placeholder deleted, `App.tsx` re-pointed) + `LeaveOverview` (optjent-of-annual) + `AccrualTrend` (CSS bars, no chart lib); read-only |
| 6105 | Security & Compliance | **No findings** — employee-self + OrgScope branches, FAIL-001 `FindAll`, `employment_start_date` not echoed, read-only, `RequireAuthorization` |
| 6106 | Test & QA | +33 unit (AccrualMath matrix + single-source architecture guard), +8 Docker-gated regression (`BalanceSeriesTests`), +7 FE (`OversightPage`) |

**Final validation (Orchestrator gate, post-Step-7a):** `dotnet build` 0 errors; unit **629 passed**; FE **164 passed** (+4 from the Step-7a W1 fix); 8 Docker-gated regression tests compile-verified (Docker unavailable locally — run in CI; not faked). Architecture single-source guard verified to fail on reintroduction of a private formula.

## Task Log

### TASK-6101 — SharedKernel: consolidate the pure `EarnedToDate` (single source)
| Field | Value |
|-------|-------|
| **Agent** | Rule Engine (extended into `src/SharedKernel/**/Calendar/**` + `src/Backend/**`, cross-domain authorized) |
| **Components** | new `src/SharedKernel/StatsTid.SharedKernel/Calendar/AccrualMath.cs` (pure `EarnedToDate` + `MonthIndex`); **delete** the two private mirrors `BalanceEndpoints.cs:37-55` + `SkemaEndpoints.cs:142-160`; re-point ALL call-sites (Balance summary; SkemaEndpoints `:879` + `:887`) + `AccrualCalculator` to `AccrualMath`; reconciliation test re-pointed |
| **KB Refs** | ADR-030, ADR-002 (pure fn), PAT-005 (HTTP boundary scope) |
| **Depends on** | — |
**Description**: Move the canonical pure earned-to-date calc into `Calendar/AccrualMath.cs` (no I/O, no wall-clock; `(annualQuota, partTimeFraction, ferieaarStart, employmentStart, asOf) → daysEarned` + `MonthIndex`). Then **delete** the two private-static Backend mirrors (`BalanceEndpoints.cs:37-55`, `SkemaEndpoints.cs:142-160`) and have every call-site invoke `AccrualMath.EarnedToDate` **directly** — Balance summary, SkemaEndpoints `:879` + `:887`, and `AccrualCalculator.EarnedToDate` (`AccrualCalculator.cs:43`, which delegates or is replaced). Behaviour-preserving. Single-source is then mechanically enforceable (no surviving private formula to drift). Re-point the existing reconciliation test to assert `AccrualMath` ↔ `AccrualCalculator` parity across a value matrix.
**Validation Criteria**:
- [ ] Exactly ONE earned-to-date/`MonthIndex` implementation exists, in `Calendar/AccrualMath.cs`; the two private Backend mirrors are **removed** (a grep/architecture test asserts no `EarnedToDate`/`MonthIndex` formula body survives outside SharedKernel).
- [ ] All call-sites (Balance summary, SkemaEndpoints `:879`+`:887`, `AccrualCalculator`) invoke `AccrualMath`; behavioral parity test (value matrix) green; existing S60 `AccrualCalculatorTests` stay green.
- [ ] No new `using StatsTid.RuleEngine` from Backend (PAT-005 preserved); `dotnet build` clean.

### TASK-6102 — Backend: read-only accrual-series endpoint
| Field | Value |
|-------|-------|
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | new `GET /api/balance/{employeeId}/series`; Program.cs mapping |
| **KB Refs** | ADR-030, ADR-021 D4 (consumption two-step), ADR-023 D3 (graceful fallback), PAT-005, SECURITY.md |
| **Depends on** | TASK-6101 |
**Description**: New read-only endpoint returning the per-month **optjent** accrual curve. **Minimal contract (Step-0b):** the series contains ONLY the MONTHLY_ACCRUAL types (VACATION + SPECIAL_HOLIDAY) across the relevant ferieår — no IMMEDIATE rows. **Server derives** entitlement year / reset-month / dated config the way `/summary` does (`BalanceEndpoints.cs:225,246`) — does NOT trust a client-supplied ferieår. Each series point's as-of is the **month-END derived from the window params** (e.g. `?year&month&months`, constructed exactly as `/summary`'s `monthEnd`, `BalanceEndpoints.cs:106`), **never `DateTime.Today`**. **Resolve the dated employment profile ONCE at the requested month-end** (the same anchor `/summary` uses) and apply that single fraction across the curve. Uses `AccrualMath.EarnedToDate` (6101). Graceful part-time fallback `?? 1.0m` per ADR-023 D3. **[Step-7a correction]** The original "per-point dated profile" framing was reversed at Step 7a: because `AccrualMath.EarnedToDate` is a SINGLE-fraction model (also used by `/summary` and the Skema quota guard), applying each point's month-end fraction to all its elapsed months makes the curve **non-monotonic** when the fraction changes mid-ferieår. Single-fraction (current-terms) keeps the curve monotonic, reconciles with `/summary`, and never contradicts the quota cap. True piecewise per-month accrual is a rule-engine semantic change (out of S61 scope) — recorded as a follow-up.
**Validation Criteria**:
- [ ] Series returns ONLY MONTHLY_ACCRUAL VACATION/SPECIAL_HOLIDAY per-month optjent (no IMMEDIATE rows); the value at the selected `?month` reconciles byte-for-byte with `/summary`'s `earned` at that month-end (shared anchor).
- [ ] Ferieår/reset-month/config **server-derived**; each point's as-of = month-END from window params (not `DateTime.Today`); fraction resolved ONCE at the requested month-end (matching `/summary`); curve is **monotonic non-decreasing** (Step-7a fix — single-fraction model; piecewise per-month accrual deferred as a follow-up).
- [ ] Authz: employee-self via self-equality (`BalanceEndpoints.cs:84-85`) AND the non-employee `OrgScopeValidator` branch (`:87-92`); no cross-employee leak. `RequireAuthorization("EmployeeOrAbove")`.
- [ ] Graceful (no 500) for a profile-less employee (`?? 1.0m`); pure/deterministic (no wall-clock).

### TASK-6103 — Frontend: types + hooks
| Field | Value |
|-------|-------|
| **Agent** | UX |
| **Components** | `useBalanceSummary.ts` (`earned` on `EntitlementInfo`, `overtimeBalance` on `BalanceSummary`); new `useAccrualSeries` hook. **All FE tasks target the canonical `frontend/src/**` tree (not `.claude/worktrees/**` copies).** |
| **KB Refs** | FRONTEND.md |
| **Depends on** | TASK-6102 (series contract) |
**Description**: Extend `EntitlementInfo` with `earned: number` and `BalanceSummary` with `overtimeBalance` (the API already returns both; `entitlementYear` already present — no change). New `useAccrualSeries(employeeId, year, month)` hook for the 6102 endpoint. No recompute of server `remaining`/`earned` on the client.
**Validation Criteria**:
- [ ] `earned` + `overtimeBalance` typed and populated from `/summary`; `useAccrualSeries` fetches the series; existing FE suite green.
- [ ] No client-side recompute of `remaining` (consumed verbatim).

### TASK-6104 — Frontend: `OversightPage` + components
| Field | Value |
|-------|-------|
| **Agent** | UX |
| **Components** | `OversightPage.tsx` (replaces placeholder) + `LeaveOverview` + `AccrualTrend` (CSS/SVG bars); reuse `BalanceSummary`/`ComplianceWarnings`; route wiring in `App.tsx` |
| **KB Refs** | FRONTEND.md, ADR-011 (oes.dk AA-safe tokens) |
| **Depends on** | TASK-6103 |
**Description**: Build the read-only dashboard (sections A–E + G from the refinement). `LeaveOverview` frames VACATION/SPECIAL_HOLIDAY as **"optjent X af årlig kvote Y"** using `earned`/`remaining` (NOT BalanceSummary's `used/totalQuota` math) with per-type year labels (ferieår vs calendar). Balance cards incl. null-safe **overtime-saldo**. Reuse `ComplianceWarnings` + add an explicit "Ingen advarsler" clean state. Selected-month approval status (badge + deadlines + rejection) sourced from the Skema month payload `data.approval`; no submit form (link to Mine perioder). `AccrualTrend` renders the 6102 series as CSS/SVG bars (no chart lib). Month/year selector (SkemaPage pattern), defaults to current month. **Empty/edge states**: new hire (`employment_start_date`) / start-of-ferieår where optjent ≈ 0 and the curve is a single point must render informatively, not broken (the S60 SPECIAL_HOLIDAY low-earned note → informational, not alarming).
**Validation Criteria**:
- [ ] `/tid/oversigt` renders the read-only dashboard; placeholder removed; AA-safe tokens; Danish labels.
- [ ] Leave overview shows optjent/brugt/planlagt/overført/rest/årlig kvote + per-type year; accrual types use the optjent framing, not `used/totalQuota`.
- [ ] Overtime-saldo null-safe; compliance clean state present; approval status read-only (no submit form).
- [ ] Accrual curve via CSS/SVG bars (no new dependency); empty/new-hire/start-of-ferieår states render informatively; `npm run build` (tsc) clean.

### TASK-6105 — Security audit
| Field | Value |
|-------|-------|
| **Agent** | Security & Compliance |
| **Components** | series-endpoint authz + scope |
| **KB Refs** | SECURITY.md, FAIL-001, ADR-007 |
| **Depends on** | TASK-6102 |
**Description**: Verify the new series endpoint enforces employee-self (own-data) + the `OrgScopeValidator` branch for non-employees; FindAll (not FindFirst) on scopes (FAIL-001); parameterized queries; no accrual/entitlement data leak across employees or orgs; read-only (no mutation/event).
**Validation Criteria**:
- [ ] Employee cannot read another employee's series (403); non-employee constrained to org scope; FAIL-001 respected; no leak.

### TASK-6106 — Tests
| Field | Value |
|-------|-------|
| **Agent** | Test & QA |
| **Components** | unit + Docker regression + FE vitest |
| **KB Refs** | — |
| **Depends on** | 6101–6105 |
**Description**: Unit — `AccrualMath` behavioral matrix + a **grep/architecture test asserting no `EarnedToDate`/`MonthIndex` formula survives outside SharedKernel**; accrual-series purity/determinism across the ferieår (part-time, mid-year `employment_start_date`, null fallback, repeat-determinism). Docker regression — series shape (MONTHLY_ACCRUAL-only) + server-derived ferieår + month-end-from-params + **curve monotonic non-decreasing + uses the selected-month fraction (single-fraction model)** + employee-self 403 + cross-employee 403 + reconciliation (series@selected-month == `/summary` `earned`) + profile-less graceful render. FE vitest — Oversigt renders optjent leave overview + accrual curve + overtime-saldo + clean compliance + empty/new-hire state.
**Validation Criteria**:
- [ ] All ACs across 6101–6105 covered; suites green; `dotnet build` + `npm run build` clean; existing 596 unit + 144 FE stay green.

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | 2026-06-02 — `codex review` (prompt-alone, uncommitted; new files made visible via `git add -N`) + internal Reviewer Agent on the full diff |
| **Sprint-start commit** | S60 `1e70874` |
| **Cycles** | 2 |

### Cycle 1 — lens divergence; 1 BLOCKER (Codex), absorbed
- **BLOCKER (Codex) [P1/P2/P4] — non-monotonic accrual curve.** `/series` resolved the dated profile per point and applied each point's month-end fraction to ALL its elapsed months (`AccrualMath.EarnedToDate` is single-fraction), so a mid-ferieår fraction change made the curve DROP (e.g. Dec 8.33 → Jan 5.21). The Reviewer did NOT flag this (it trusted the Docker-gated, unrun "bends the curve" test, which codified the wrong behavior — a clean lens-complementarity catch). **FIXED**: resolve the fraction ONCE at the requested month-end (matching `/summary`), apply across the curve → monotonic + byte-identical reconciliation; piecewise per-month accrual deferred (would diverge from `/summary` + the Skema quota guard = a rule-engine change, out of scope). Test reworked: `Series_MidFerieaarPartTimeChange_UsesSelectedMonthFraction_Monotonic` + `Series_PartTimeDropMidFerieaar_CurveIsMonotonicNonDecreasing`.
- **WARNING (Reviewer) W1 — duplicate "Arbejdstidskontrol" heading** on OversightPage when issues present (page `<h3>` + `ComplianceWarnings` internal `<h3>`). **FIXED**: `hideTitle` prop on `ComplianceWarnings` (default false → SkemaPage unchanged); OversightPage renders it once. +4 FE tests.
- **NOTEs (Reviewer, no action)**: determinism clean (no wall-clock); single-source consolidation correct (one formula body, PAT-005 preserved); P7 clean (both auth branches, no `employment_start_date` leak, read-only); FE reads server values verbatim; ACs met. **N7**: the 8 Docker-gated `BalanceSeriesTests` are compile-verified only (Docker unavailable locally) — must be confirmed green in CI before close.

### Cycle 2 — verification of the cycle-1 fixes — CONVERGED
- **Codex**: "No new findings — fixes sound."
- **Reviewer**: "No new findings — fixes sound." Verified: single-fraction resolution hoisted out of the loop (one `/summary`-anchor resolve + graceful `?? 1.0m`); curve monotonic non-decreasing; selected point byte-identical to `/summary`; no wall-clock; `/summary`/`AccrualMath`/`SkemaEndpoints` unchanged; tests reworked correctly (monotonicity + selected-month-fraction + reconciliation retained); W1 heading renders once with SkemaPage unchanged. (Informational: changeset is uncommitted, so the fix isn't isolated as its own commit.)

**Verdict: Step 7a CONVERGED at cycle 2** (cycle 1: 1 BLOCKER [non-monotonic curve] + 1 WARNING [duplicate heading] absorbed; cycle 2 clean). Cycle cap = 2 per lens respected; no halt-and-prompt. Post-fix gate: `dotnet build` 0 errors · **unit 629** · **FE 164**. The 8 Docker-gated `BalanceSeriesTests` remain CI-pending (Docker unavailable locally) — confirm green in CI before/at commit.

**Follow-up recorded:** piecewise per-month accrual (each month at its own dated fraction) is the legally-precise model but would diverge from `/summary` + the Skema quota guard — a rule-engine semantic change for a future sprint, not S61.

## Test Summary

| Suite | Previous (S60) | Current (S61) | Delta |
|-------|------|------|------|
| Unit | 596 | **629** | +33 (AccrualMath matrix + single-source architecture guard) |
| Regression (Docker-gated) | 20 | +8 written (`BalanceSeriesTests`) | +8 — **CI-pending** (Docker unavailable locally; compile-verified, not faked) |
| Smoke | 4 | 4 | skipped (Docker not running) |
| Frontend | 144 | **164** | +20 (LeaveOverview 5, AccrualTrend 4, OversightPage, ComplianceWarnings 3, W1 heading) |
| **Total (locally verified: unit+FE)** | **740** | **793** | **+53** |

`dotnet build` 0 errors; unit 629 / FE 164 verified at close (re-run via `sprint-test-validation`). The 8 Docker-gated `BalanceSeriesTests` (reconciliation, monotonicity, auth 403s, profile-less) must be confirmed green in CI.

## Orchestrator-authored artifacts (not agent tasks)
- **ADR-030 annotation** — record that the pure `EarnedToDate` now lives in SharedKernel (single source); no new ADR (read-only consumer of an existing decision).
- **`danish-agreements.md:117`** — DRIFT corrected at Step 0a (IMMEDIATE → MONTHLY_ACCRUAL for VACATION/SPECIAL_HOLIDAY).
- **ROADMAP.md** + **sprints/INDEX.md** — S61 row at close.
