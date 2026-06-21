# Sprint 88 — Leder-oversigt P2: the expandable detail row

| Field | Value |
|-------|-------|
| **Sprint** | 88 |
| **Status** | closed |
| **Start Date** | 2026-06-20 |
| **End Date** | 2026-06-21 |
| **Orchestrator Approved** | yes (Step-0b BLOCKERs B1+B2 resolved, baked into TASK-8801/8802) |
| **Build Verified** | yes (`dotnet build` 0 err; `tsc` + `npm run build` clean) |
| **Test Verified** | yes — local full pyramid green (regression 1015 effective; 6 FAIL-002 testcontainer sheds isolation-cleared 31/31); push + CI-verify to follow |

## Sprint Goal
P2 of the `design_handoff_leder_oversigt` redesign (the 2-sprint split, OQ-4): the **expandable employee detail row** on the S87 TeamOversigt page — the accordion + the detail panel (Saldi grid + project-allocation Fordeling + the §-alerts + the footer), with the heavy per-employee data **lazy-fetched on expand** (the OQ-2 ruling: full compliance lazy, not in the table aggregate). Closes the leder-oversigt feature. Mostly frontend + ONE small backend read (the allocation breakdown).

Refinement: `.claude/refinements/REFINEMENT-leder-oversigt.md` (P2 = the expandable detail). Owner ruled OQ-2 = cheap warning on the column + **full compliance lazy-on-expand** (this sprint), OQ-4 = two sprints (this = P2).

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| check_docs hard checks | CLEAN | through S87; db-schema in sync |
| Working tree | CLEAN | at S87 tip `ce83b7c` |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (a new backend read endpoint; the allocation-breakdown gate-mirror [drift risk]; the lazy per-employee compliance [rule-engine on expand — the S73 fault-isolation concern]; the accordion perf) |
| **External Codex** | invoked 2026-06-21 — `.claude/s88-step0b-codex.log` |
| **Internal Reviewer** | invoked 2026-06-21 (Plan agent, read-only) |
| **BLOCKERs resolved before Step 1** | YES — both baked into TASK-8801/8802 below |

### Findings (cycle 1) — BOTH lenses converged INDEPENDENTLY on the same two BLOCKERs ([[review-lens-complementarity]] — strongest possible signal)

**BLOCKER B1 — gate-mirror drift (the "mirror EXACTLY" claim was mathematically false as drafted).** TASK-8801 defined `unallocated = max(0, worked − allocated)` at MONTH level, but the table `hasWarning` it must equal is a PER-DAY, SYMMETRIC check: `Math.Abs(worked − allocated) > AllocationTolerance` on ANY day (`ApprovalEndpoints.cs:1097-1116`), mirroring the approve gate (`ApprovalEndpoints.cs:1326`; ADR-028 D4 "blocks in BOTH directions"). They provably disagree: (a) any day with `allocated > worked` (over-allocation) → `hasWarning=true` but month `max(0,Σworked−Σallocated)=0`; (b) day A under 3h + day B over 3h nets to month 0 → `hasWarning=true`, `unallocated=0`. The opened detail would contradict the row chip. **RESOLVED** → TASK-8801 returns the per-day basis: `underAllocated = Σ_days max(0, round(worked_d)−round(allocated_d))`, `overAllocated = Σ_days max(0, round(allocated_d)−round(worked_d))`, `hasAllocationImbalance = (underAllocated>tol) OR (overAllocated>tol)` — `hasAllocationImbalance` equals the table `hasWarning` EXACTLY; the detail drives its imbalance state off it (NOT off `unallocated>0` alone). `allocations[] by taskId` stays a month-sum display aid only (does NOT prove the per-day predicate).

**BLOCKER B2 — compliance lazy-fetch is NOT authorized for designated-only leaders (silent 403).** `GET /api/compliance/{employeeId}/period` authorizes non-employees only via `OrgScopeValidator.ValidateEmployeeAccessAsync` (`ComplianceEndpoints.cs:36`), which is org-subtree only (`OrgScopeValidator.cs:73-118`). The team-overview roster is the DESIGNATED-approver set, which (ADR-027 D13, `ADR-027:128`) admits cross-afdeling vikar/escalation approvers whose org-scope does NOT cover the employee. So a leader acting purely via a designated edge sees the row, expands it, and `/compliance/{id}/period` returns 403 → the Advarsel panel degrades to "kunne ikke hentes" for exactly the vikar rows (a systematic auth hole masked as a transient fault). **RESOLVED** → fold an ADDITIVE designated-approver OR-branch into TASK-8801's backend scope: `ValidateEmployeeAccessAsync(...) OR IsEffectiveDesignatedApproverAsync(actorId, employeeId, today)` (the same OR-pattern the approve endpoint already uses, `ApprovalEndpoints.cs:263-271`) — preserves every existing caller (employee/HR/org-scope), adds the designated path. Tested: a cross-afdeling vikar approver reading compliance succeeds; a non-designated leader still 403s.

**WARNING (both) — name the EXACT predicate.** The canonical employee-keyed predicate is `DesignatedApproverAuthorizer.IsEffectiveDesignatedApproverAsync(actorId, employeeId, today)` (`DesignatedApproverAuthorizer.cs:75`); the S87 roster filters through this exact predicate (`ApprovalPeriodRepository.cs:432`). **RESOLVED** → TASK-8801 authorizes the breakdown with THIS predicate (NOT `ValidateEmployeeAccessAsync`/org-scope), so `breakdown-authorized == roster-authorized` for the rows the leader sees.

**WARNING (both) — TASK-8801 validation missed the drift cases.** **RESOLVED** → validation now requires: under-allocation one day; over-allocation one day; over+under netting to month-0; allocation on a zero-worked day; `allocations[]` month-grouping coexisting with the per-day imbalance figures.

**WARNING (internal) — "no second save path" under-specified.** The P1 status-aware handlers live inline in `TeamOversigt.tsx:175-309` (NOT in `useApprovals`, whose `approvePeriod`/`rejectPeriod` handle neither 428 nor 409). **RESOLVED** → TASK-8802 passes `handleApprove`/`openReject`/`handleReopen` (+ the enforcement/reject Dialog state stays in the parent) into `TeamRowDetail` as props; a vitest asserts the 428→confirm + 409 paths fire from the footer.

**NOTE (both) — Saldi "no extra fetch" CONFIRMED true.** The row carries `agreement, normExpected, normRegistered, flexBalance, overtime, ferieUsed, ferieTotal` (`useTeamOverview.ts:23`, backend `ApprovalEndpoints.cs:1126`) — the Merarbejde[AC]/Overarbejde switch works with zero extra fetch. (Caveat N4: `overtime`/`normExpected` are the COARSE batched aggregate values — table-consistent, NOT the `/summary` authoritative dated-config figures; TASK-8802 notes this so reviewers don't expect them to match a separately-opened balance page.)

**NOTE (internal) — ApprovalDashboard deletion closure.** `ApprovalDetailPanel.tsx` is imported ONLY by `ApprovalDashboard` and shares `ApprovalDashboard.module.css`. **RESOLVED** → TASK-8803's deletion set is enumerated (5 files); decision still deferred to close.

**NOTE (internal) — accordion a11y contract + 8801→8802 dependency.** **RESOLVED** → TASK-8802 a11y criteria pinned (real `<button>` toggle w/ `aria-expanded`+`aria-controls`→detail `<tr>` id; Escape collapses + returns focus to the toggle; checkbox + Handling `<td>` `stopPropagation`); TASK-8802 declares "Depends on: TASK-8801 (endpoint + the exact JSON contract below)".

## Architectural Constraints
- [ ] P5/P2 — the allocation breakdown mirrors the approve-gate predicate EXACTLY via the PER-DAY basis (B1): `hasAllocationImbalance` (= `underAllocated>tol` OR `overAllocated>tol`, both Σ per rounded day) **equals** the table `hasWarning` (`ApprovalEndpoints.cs:1097-1116` / gate `:1326`); the detail's imbalance state drives off `hasAllocationImbalance`, never off a month-level `unallocated` scalar
- [ ] P7 — the breakdown endpoint is designated-approver-scoped via `DesignatedApproverAuthorizer.IsEffectiveDesignatedApproverAsync(actorId, employeeId, today)` (NOT `ValidateEmployeeAccessAsync`/org-scope), so `breakdown-authorized == team-overview roster` (no org-scope leak, no 403 on a visible row)
- [ ] P7 — the compliance period endpoint (B2) gets an ADDITIVE designated-approver OR-branch so a cross-afdeling vikar/escalation approver who sees the row can fetch its Advarsel; every existing caller (employee/HR/org-scope) preserved
- [ ] P5 — the lazy compliance on expand is **per-employee fault-isolated** (one rule-engine failure degrades THAT row's Advarsel to a soft "kunne ikke hentes", not a page error — the S73 503-coupling stays contained to the opened row)
- [ ] P9 — hifi fidelity (the detail panel tokens/copy); a11y (the accordion: real `<button>` toggle w/ aria-expanded + aria-controls, focus, Escape-collapse + focus-return; the S82 kit)
- [ ] No regression to the S87 table/approvals or the balance/compliance/skema endpoints

## Task Log

### TASK-8801 — allocation-breakdown endpoint (backend)

| Field | Value |
|-------|-------|
| **ID** | TASK-8801 |
| **Status** | DONE (build 0 err; `AllocationBreakdownEndpointTests` 17/17; no-regression `TeamOverviewAggregateTests`+`RuleEngineAuthForwardingTests` 22/22) |
| **Agent** | Backend + API Integration |
| **Components** | `ApprovalEndpoints.cs` (new GET), `ComplianceEndpoints.cs` (B2 auth OR-branch), tests |
| **KB Refs** | ADR-028 D4 (allocation gate — per-day, both directions), ADR-027 D13 (designated authority / see==act) |
| **Orchestrator Approved** | no |

**Description (Step-0b BLOCKER fixes baked in):**

**(1) `GET /api/approval/{employeeId}/allocation-breakdown?year=&month=`** (LeaderOrAbove). **Auth (B1/B2 predicate):** authorize via `DesignatedApproverAuthorizer.IsEffectiveDesignatedApproverAsync(actorId, employeeId, today)` → 403 if false. Do NOT use `ValidateEmployeeAccessAsync`/org-scope — this is the exact predicate the S87 team-overview roster filters through (`ApprovalPeriodRepository.cs:432`), so `breakdown-authorized == roster` (no 403 on a visible row, no leak).

**Response contract (PIN — FE depends on these exact field names):**
```jsonc
{
  "allocations": [ { "taskId": "...", "hours": 12.5 } ], // NORMAL + non-null TaskId, grouped by TaskId, MONTH sum — display bars only
  "worked":     160.0,   // MONTH sum: work_time_projection interval-hours + manual_hours
  "allocated":  150.0,   // MONTH sum: NORMAL + non-null TaskId hours
  "underAllocated": 10.0,// DISPLAY ONLY: Σ_days max(0, round(worked_d,2) − round(allocated_d,2)) — the "Ikke fordelt"/Manglende-fordeling number
  "overAllocated":  0.0, // DISPLAY ONLY: Σ_days max(0, round(allocated_d,2) − round(worked_d,2))
  "hasAllocationImbalance": true // AUTHORITATIVE: the per-day ANY check — EQUALS the table hasWarning EXACTLY
}
```
**Mirror the gate via the PER-DAY basis (B1 — the month scalar `max(0,worked−allocated)` is provably WRONG):** reuse the S87 aggregate's per-(employee,day) worked/allocated maps (`ApprovalEndpoints.cs:910-955`); `worked_d` = work_time_projection intervals + manual_hours; `allocated_d` = NORMAL + non-null `TaskId`; `AllocationTolerance = 0.005m`. **`hasAllocationImbalance` is computed IDENTICALLY to the aggregate's `hasWarning` loop (`ApprovalEndpoints.cs:1102-1119`): iterate the days with either worked or allocated; `true` iff ANY day has `Math.Abs(round(worked_d,2) − round(allocated_d,2)) > AllocationTolerance`.** It MUST NOT be derived as `(underAllocated>tol) OR (overAllocated>tol)` — summing sub-tolerance daily deltas could trip a sum past tol that the per-day ANY check (and thus the table chip) would not, re-introducing drift in the opposite direction. The summed `underAllocated`/`overAllocated` are DISPLAY aids only (rounded to 2dp); `allocations[]` is a month-sum-by-TaskId display aid (sums to `allocated`). Test: `hasAllocationImbalance == hasWarning` for the same employee/month across all drift fixtures.

**(2) B2 — compliance endpoint auth OR-branch.** In `ComplianceEndpoints.cs` (`GET /api/compliance/{employeeId}/period`, the non-employee branch ~`:36`), add `IsEffectiveDesignatedApproverAsync(actorId, employeeId, today)` as an ADDITIVE OR alongside the existing `ValidateEmployeeAccessAsync` (mirror the approve endpoint's OR-pattern `ApprovalEndpoints.cs:263-271`). Preserve every existing caller; add the designated-approver path so the lazy Advarsel fetch works for cross-afdeling vikar rows. Apply the same OR to `/compliance/{employeeId}/compensatory-rest` only if it shares the branch and the FE would hit it (else leave untouched — minimal surface).

**Validation Criteria**:
- [ ] **Drift coverage (B1):** tests pin `hasAllocationImbalance == hasWarning` for — (a) under-allocation one day; (b) **over-allocation one day** (`allocated_d>worked_d` → imbalance true, `underAllocated=0`); (c) **over+under netting to month-0** (imbalance true though `Σworked==Σallocated`); (d) allocation on a zero-worked day; (e) a clean fully-allocated month (imbalance false). At least one test is RED on the naive `max(0,worked−allocated)` month scalar.
- [ ] `allocations[]` per-task hours sum to `allocated` and coexist with the per-day imbalance figures.
- [ ] **Auth (breakdown):** a non-designated leader → 403; a designated approver incl. cross-afdeling vikar-coverage → 200 with the breakdown. A row present in `team-overview` is always breakdown-authorized (roster ⊇).
- [ ] **Auth (B2 compliance):** a cross-afdeling vikar/escalation designated approver → 200 on `/compliance/{id}/period`; a non-designated leader → 403; existing employee/HR/org-scope callers unchanged (RED-on-old: a vikar-approver test fails before the OR-branch).
- [ ] `dotnet build` 0/0; tests green; no regression to the S87 aggregate/gate or existing compliance callers.

**Files Changed**: `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs`, `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs`, `tests/**`

---

### TASK-8802 — expandable detail row + detail panel (frontend)

| Field | Value |
|-------|-------|
| **ID** | TASK-8802 |
| **Status** | DONE (FE vitest 495/495 [37 files]; tsc + build clean; +22 new TeamRowDetail tests, −21 from the ApprovalDashboard/DetailPanel deletion) |
| **Agent** | UX/Frontend |
| **Components** | `frontend/src/pages/approval/TeamOversigt.tsx` (+ css), a `TeamRowDetail` subcomponent + `useAllocationBreakdown` (new, lazy) + `useCompliance` (existing, lazy), tests |
| **KB Refs** | ADR-011, S82 (a11y kit), the `design_handoff_leder_oversigt/README.md` §2 |
| **Orchestrator Approved** | no |
| **Depends on** | TASK-8801 (the `/allocation-breakdown` endpoint + the PINNED JSON contract above; the B2 compliance auth OR-branch) |

**Description**: Make the TeamOversigt rows **expandable** (accordion — opening one closes others). On expand, render `TeamRowDetail` per the hifi §2:
1. **Saldi** (4-cell hairline grid): Flex saldo (colored) / Ferie (`{ferieUsed}/{ferieTotal} dage`) / Normtimer (`{normRegistered}/{normExpected} t`) / **Merarbejde** (`agreement === 'AC'`) or **Overarbejde** (else) (`{overtime} t`). **Reuse the row's existing figures — NO extra fetch.** Note (N4): these are the COARSE batched aggregate values (table-consistent), NOT the `/summary` authoritative dated-config figures — do not imply more precision.
2. **Fordeling af arbejdstid** — lazy-fetch `/allocation-breakdown` on expand (new `useAllocationBreakdown`): per-project bars (label by taskId + hours + 5px bar, summing to `allocated`) + the **Ikke fordelt** entry showing `underAllocated`, **amber + bold when `hasAllocationImbalance`** (NOT `underAllocated>0` alone — so over-allocation/netting cases still flag, matching the row chip), muted when not. Header sum `{allocated} / {worked} t`.
3. **Alerts** (drive off the per-day contract, NOT a month scalar — B1; gate both behind `hasAllocationImbalance` so the detail never shows "clean" while the row chip warns, and never warns while the chip is clean):
   - **Manglende fordeling** (amber) when `hasAllocationImbalance && underAllocated > 0`: `{underAllocated} t af {worked} t er ikke fordelt på projekter. Medarbejderen skal fordele hele sin registrerede tid.`
   - **Overfordeling** (amber, NEW) when `hasAllocationImbalance && overAllocated > 0`: `{overAllocated} t er fordelt på projekter ud over den registrerede tid.` — covers the over-allocation/netting case that trips the chip but leaves `underAllocated = 0`.
   - **Advarsel** (amber) — lazy-fetch `GET /api/compliance/{employeeId}/period` on expand (existing `useCompliance`); render the working-time/quota warnings. **Per-employee fault-isolated:** a failed/timed-out call → a soft "Advarsler kunne ikke hentes", NOT a page error; the rest of the panel still renders.
   - **Begrundelse for afvisning** (red) from `row.rejectionReason` when REJECTED.
4. **Footer**: the status line (`Indsendt {submittedAt} · lederfrist …` / `Godkendt {decisionAt}` / `Afvist {decisionAt} · afventer ny indsendelse` / `Ikke indsendt endnu · kladde`) + the large **approve / reject / reopen** buttons. **No second save path (W):** `TeamRowDetail` receives the parent's existing `handleApprove(row)` / `openReject(row)` / `handleReopen(row)` as PROPS (the enforcement-confirm + reject Dialog state stays in `TeamOversigt`); it must NOT re-implement mutations nor route through `useApprovals.approvePeriod` (which handles neither 428 nor 409). Reopen stays LocalHR+ (preserve P1 policy).

Lazy: fetch breakdown + compliance only when a row opens (cache per employee+month for the session; refetch on month change). No upfront N fetches.

**a11y contract (pin):** the expand toggle is a real `<button>` carrying `aria-expanded` + `aria-controls`→the detail `<tr>`'s id; **Escape** collapses the open row and returns focus to its toggle; the checkbox `<td>` and Handling `<td>` `stopPropagation` so their controls don't toggle the row (hifi §80).

**Validation Criteria**:
- [ ] The accordion (one open; chevron rotate; toggle + stop-propagation correct); the detail matches the hifi §2 (Saldi reuses row data; Fordeling from the lazy breakdown; the alerts; the footer).
- [ ] **Imbalance UI (B1):** the "Ikke fordelt" amber state + the alerts derive from `hasAllocationImbalance`/`underAllocated`/`overAllocated` — a vitest proves the over-allocation case (`underAllocated=0`, `hasAllocationImbalance=true`) renders amber + the Overfordeling alert (NOT a clean panel).
- [ ] Lazy: breakdown + compliance fetched on expand only; compliance fault-isolated (a failure → soft message, the rest renders).
- [ ] **No second save path (W):** the footer calls the parent handlers by prop; a vitest asserts the 428→confirmFallback retry + the 409-surfaced paths fire from the footer; after a mutation the table refetches + the row reflects it.
- [ ] FE vitest (expand/collapse, lazy fetch loading/empty/error states, the Merarbejde|Overarbejde switch, all alert conditions incl. over-allocation, the footer reuse) + a11y (aria-expanded + aria-controls, focus, Escape + focus-return, stop-propagation); `tsc` + build clean.

**Files Changed**: `frontend/src/pages/approval/TeamOversigt.tsx` (+ css), `frontend/src/hooks/useAllocationBreakdown.ts` (new), `frontend/src/pages/approval/__tests__/**`

---

### TASK-8803 — Validate + Step-7a + docs + close (Orchestrator)

| Field | Value |
|-------|-------|
| **ID** | TASK-8803 |
| **Status** | DONE — Step-7a dual-lens CLEAN (both APPROVE); ApprovalDashboard deleted (5-file closure); docs updated; ADR-028 D4 amended |
| **Agent** | Orchestrator |
| **Components** | validation, `docs/QUALITY.md`, `ROADMAP.md`, `docs/sprints/SPRINT-88.md` |
| **Orchestrator Approved** | no |

**Description**: Build + FE vitest + the approval backend regression + e2e. Step-7a dual-lens (the breakdown gate-mirror per-day basis / the B2 compliance auth / the lazy-compliance fault-isolation / the accordion a11y / hifi fidelity / no second save path). **ApprovalDashboard deletion (decide at close):** if deleting, the closure is FIVE files (NOTE from Step-0b) — `ApprovalDashboard.tsx`, `ApprovalDashboard.module.css`, `ApprovalDetailPanel.tsx` (imported ONLY by ApprovalDashboard, shares that CSS), `__tests__/ApprovalDashboard.test.tsx`, `__tests__/ApprovalDetailPanel.test.tsx`; grep-zero both component names before removing the CSS, and confirm the `godkend/godkendelser` redirect (already in `App.tsx`) stays. If deferring, say so explicitly. Docs (QUALITY/ROADMAP/SPRINT-88; ADR-028 D4 — append the breakdown endpoint + the per-day exact-mirror definition so the month-vs-day drift can't be re-introduced; FRONTEND if a reusable pattern). Commit + push + CI-verify; MEMORY.

**Validation Criteria**:
- [ ] Full pyramid + e2e green; CI green on push. Docs updated; `check_docs` green. The leder-oversigt feature is complete.

**Files Changed**: `docs/**`, `ROADMAP.md`, (maybe) the ApprovalDashboard deletion

## External Review (Step 7a)
| Lens | Verdict | Cycle | Artifact |
|------|---------|-------|----------|
| Internal Reviewer (read-only Plan agent) | APPROVE — B1+B2 resolved; 4 non-blocking NOTEs | 1 (clean) | `.claude/reviews/SPRINT-88-step7a-reviewer.md` |
| External Codex (`codex review`, uncommitted) | APPROVE — no actionable regressions; B1+B2 resolved | 1 (clean) | `.claude/reviews/SPRINT-88-step7a-codex.md` |

Both lenses independently confirmed the two Step-0b BLOCKERs are genuinely closed: **B1** — the breakdown's `hasAllocationImbalance` is the per-day ANY check, IDENTICAL to the team-overview `hasWarning` (not a sum-vs-tol derivation); the under/over totals + `allocations[]` are display-only. **B2** — the `/period` compliance OR-branch is strictly additive (employee-self + org-scope preserved; `/compensatory-rest` untouched). NOTEs resolved before commit: the dangling collapsed-state `aria-controls` (now gated to `isExpanded`) and the stale App.tsx deletion comments. Remaining NOTEs (intentional "0,0 t" amber on over-allocation; no abort-guard on the benign lazy-hook late-resolve) accepted. Zero BLOCKERs to absorb.

## Test Summary
Pyramid (S87 → S88):

| Tier | S87 | S88 | Δ | Note |
|------|-----|-----|---|------|
| Unit (`StatsTid.Tests.Unit`) | 856 | 856 | 0 | no unit-tier change |
| Regression (`StatsTid.Tests.Regression`) | 998 | 1015 | +17 | `AllocationBreakdownEndpointTests` (17) — drift + auth. Full run 1009/1015 + **6 FAIL-002 testcontainer-start sheds** (`DockerHarness.StartAsync`, all 4 classes DISJOINT from the Approval/Compliance change) **isolation-cleared 31/31** on an exclusive re-run → effective 1015/1015 |
| Smoke (`StatsTid.Tests.Smoke`) | 6 | 6 | 0 | no init.sql/schema/event change |
| Frontend (vitest) | 494 | 495 | +1 | +22 `TeamRowDetail`, −21 from the ApprovalDashboard/DetailPanel deletion |
| e2e (Playwright) | 3 | 3 | 0 | the S87 journey already drives `godkend/oversigt` |

Build: `dotnet build` 0 errors; `tsc --noEmit` clean; `npm run build` clean. Backend no-regression: `TeamOverviewAggregateTests` + `RuleEngineAuthForwardingTests` green alongside the new suite.

## Sprint Retrospective
**Closed the leder-oversigt feature (P2 of the 2-sprint split).** The expandable per-employee detail row on the S87 Teamoversigt page (accordion + Saldi grid + project-allocation Fordeling + the §-alerts + the footer), one new backend read (`GET /api/approval/{employeeId}/allocation-breakdown`), an additive compliance-auth fix, and the deletion of the now-superseded `ApprovalDashboard` (the planned S88 cleanup).

**The two Step-0b BLOCKERs were the whole story — and both lenses caught them independently ([[review-lens-complementarity]] at its strongest):**
- **B1 (gate-mirror drift):** the plan's `unallocated = max(0, worked − allocated)` MONTH scalar would have visibly contradicted the table chip (over-allocation, or per-day imbalances netting to ~0, trip the table's per-day symmetric `hasWarning` while the month scalar shows 0). Resolved to a per-day basis where `hasAllocationImbalance` is the IDENTICAL per-day ANY check — and the Orchestrator caught a further subtlety the reviewers' proposed fix missed: deriving the boolean as `(underAllocated>tol) OR (overAllocated>tol)` would drift in the OPPOSITE direction (sub-tolerance daily deltas summing past tol), so the boolean is the per-day ANY check and the sums are display-only.
- **B2 (compliance auth gap):** the lazy `/compliance/{id}/period` fetch authorizes via org-scope only, but the roster is the designated-approver set (cross-afdeling vikar/escalation) — a designated-only leader would have silently 403'd the Advarsel panel for exactly the vikar rows. Resolved with an additive `IsEffectiveDesignatedApproverAsync` OR-branch (the approve endpoint's pattern), preserving every existing caller.

**Both fixes carry RED-on-old discriminating tests** (the over-allocation + netting drift cases would be RED on a month scalar; the vikar-approver compliance test was 403 before the OR-branch). Step-7a came back clean on both lenses — only NOTEs, two fixed pre-commit. The leder-oversigt feature (the biggest of the four design handoffs, and the only one needing real backend work) is complete.

**Follow-up:** a manual visual spot-check of the detail panel vs the prototype (`design_handoff_leder_oversigt/Leder-oversigt.dc.html`) — not driveable at impl time; the CI e2e drives the real page but doesn't assert pixel fidelity. **Standing candidate:** the Frontend A−→A pass (visual-regression baseline + component-docs — the named S82 residual).
