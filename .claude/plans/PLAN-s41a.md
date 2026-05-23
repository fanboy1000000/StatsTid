# PLAN — Sprint 41a: ADR-024 Amendment (Cutover Seams)

| Field | Value |
|-------|-------|
| **Sprint** | 41a (sub-sprint splitting off the architectural amendment work; mirrors S38b precedent — preserves S42 main-sequence numbering for the deferred D1+D2 cutover sprint) |
| **Phase** | 4e (Phase D ADR-024 amendment per smoke-alarm-defer cycle-1 escalation) |
| **Sprint type** | **DESIGN-ONLY** — amends ADR-024 settling 4 architectural seams that cycle-1 of S41 refinement surfaced as load-bearing BLOCKERs. No code changes. |
| **Base commit** | `dfe5efa` (S40 close) |
| **Refinement** | `REFINEMENT-s41-d1d2-cutover-pre-amendment-cycle-trail.md` (predecessor; superseded — surfaced the 4 seams but didn't settle them) |
| **Sprint open date** | 2026-05-23 |
| **Projected end date** | 2026-05-23 (single-day; design-only single-ADR sprint) |
| **Task count** | 4 (TASK-41A-00..03) |

## Sprint Goal

Author an amendment section on `docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md` settling the 4 cutover seams that S41 refinement cycle 1 surfaced as load-bearing BLOCKERs. Decisions made HERE; implementation refinement re-drafts against the amended ADR in S42.

## The 4 Seams

Cycle 1 of S41 refinement (both lenses convergent) surfaced:

1. **Seam A — Rule-engine consumer of MERARBEJDE suppression**: ADR-024 D2 L69 says "OvertimeGovernanceRule reads the tri-state" but the actual MERARBEJDE wage-line emitter is `OvertimeRule.cs:45` (RuleId="OVERTIME_CALC"); `OvertimeGovernanceRule.cs` is a WARNING-only ceiling checker. The ADR mis-identified the rule.
2. **Seam B — DISCRETIONARY event-emit seam**: ADR-024 D2 L69 says `PayrollMappingService.BuildLine` reads the tri-state + emits `MerarbejdeDiscretionary` event in atomic tx. But `BuildLine` is `internal static` pure constructor — no DB / outbox / audit capability at that site.
3. **Seam C — ConfigResolutionService signature mismatch**: ADR-024 D2 L69 calls `ConfigResolutionService.GetEffectiveConfig(employee_id, date)`. Actual signature: `ResolveAsync(orgId, agreementCode, okVersion, position?, ct)` — no employeeId, no date, no employmentCategory.
4. **Seam D — employment_category determinism gap**: `EmploymentProfileResolver` joins `users.employment_category` LIVE (per docstring L26-29); dated lookup on the tri-state row is moot if the lookup KEY itself is live. ADR-024 D1 L40 acknowledges the gap ("future Phase 4e work will move ok_version / employment_category / primary_org_id into dated history tables too") but the rule-path semantics under PROVISIONAL pre-launch posture are unspecified.

## Phase Decomposition

Orchestrator-direct sequential per S38b / S32 / S28 design-only precedent.

| Phase | Tasks | Dispatch |
|-------|-------|----------|
| 0 | TASK-41A-00 | Sprint open (this file + SPRINT-41a.md + INDEX) |
| 1 | TASK-41A-01 | ADR-024 amendment authorship (Orchestrator-direct per WORKFLOW.md KB write rule) |
| 2 | TASK-41A-02 | Step 7a-equivalent dual-lens review (Codex + Reviewer Agent; cycle-cap 2 per lens; MANDATORY per `sprint-close-guard.ps1` hook) |
| 3 | TASK-41A-03 | Sprint close |

## Step 0a — Entropy Scan

| Check | Result |
|-------|--------|
| KB path validation | CLEAN — ADR-024 ACCEPTED at S38 + projection disclaimer at a0e30ed; amendment block convention established (S38 TASK-3803 ADR-013 + a0e30ed for ADR-024/025/026) |
| Sub-sprint numbering convention | CONFIRMED — S38b precedent for design-only sub-sprints (S41a = first sub-sprint of S41) |
| Mechanical gate | ACTIVE per `297fdee` + extended `d5c6a87`; design-only sprint still requires Step 7a-equivalent artifacts |
| Cycle trail | NOTED — this sprint is the smoke-alarm response to cycle 4 of same-area ADR-024 thrash (S38b ADR-026 + S40 cycle 1+2 + S41 cycle 1) |

## Step 0b — Plan Review

**SKIP** — design-only sprint per S28 / S32 / S38 / S38b precedent. Step 7a-equivalent at close (TASK-41A-02) is the formal review gate.

## Architectural Constraints

_Checked at close._

- [ ] **P1 — Architectural integrity** → ADR amendment cross-references preserved (ADR-001 / ADR-014 / ADR-016 D10 / ADR-018 D3 / ADR-019 D2 D8 / ADR-020 D2 / ADR-023 D1 D3 D8 all referenced from the amendment)
- [ ] **P2 — Deterministic rule engine** → Seam A decision matches OvertimeRule + ConfigResolutionService consumption; HasMerarbejde boolean disabler path preserved
- [ ] **P3 — Event sourcing** → Seam B decision honors PAT-004 emit-from-orchestration-not-rules pattern
- [ ] **P4 — Version correctness** → Seam C signature change preserves dated-lookup semantics per S33 ADR-023 D1
- [ ] **P7 — Security** → No security-surface changes (design only)

## Task Log

### Phase 0 — Sprint Open

#### TASK-41A-00 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-41A-00 |
| **Status** | pending |
| **Components** | `.claude/plans/PLAN-s41a.md` (this file), `docs/sprints/SPRINT-41a.md`, `docs/sprints/INDEX.md` provisional entry |

### Phase 1 — ADR-024 Amendment (TASK-41A-01)

Append `## Amendment 2026-05-23 — Cutover Seams (S41a / Phase D Implementation Sprint 2 pre-flight)` section at the bottom of `docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md` per ADR-013 amendment precedent (S38 TASK-3803).

Section content settles all 4 seams with explicit text per the recommendations below. After amendment, the ADR's S40 cutover (now S42 cutover) sub-sprint scope dispatches against the amended seams.

**Seam A — Rule consumer**: amendment specifies `OvertimeRule.cs` (RuleId="OVERTIME_CALC") consumes tri-state INDIRECTLY via the `HasMerarbejde` boolean disabler emerging from role-override merge. ConfigResolutionService merge logic: when `merarbejde_compensation_right ∈ {DISCRETIONARY, NONE}`, the merged AgreementRuleConfig has `HasMerarbejde=false`; OvertimeRule's existing `if (HasMerarbejde && HasOvertime)` short-circuit suppresses MERARBEJDE emission. No change to OvertimeRule code. Rule-engine layer stays tri-state-naive.

**Seam B — DISCRETIONARY event-emit**: amendment specifies the event-emit seam is `PeriodCalculationService.MapCalculationResultAsync` (or equivalent post-rule-engine orchestration point), NOT `PayrollMappingService.BuildLine`. PCS has DB+outbox access via injected dependencies; BuildLine is pure-function on purpose. PCS inspects merged config tri-state; for `DISCRETIONARY` segments where rule would have emitted MERARBEJDE if CONTRACTUAL, PCS emits `MerarbejdeDiscretionary` event in the atomic tx alongside payroll-line construction. Test fixture exercises this via PCS direct invocation.

**Seam C — ConfigResolutionService signature**: amendment specifies a new method `ResolveAsync(string orgId, string agreementCode, string okVersion, string? position, string? employmentCategory, DateOnly asOfDate, CancellationToken ct)` as the dated-lookup overload. Existing `ResolveAsync(orgId, agreementCode, okVersion, position, ct)` overload preserved for backward-compat with non-dated callers (admin endpoints, JWT mint). Migration path: PCS + PayrollMappingService callers switch to the dated overload; admin endpoints stay on the legacy overload until their own dated-lookup needs land.

**Seam D — employment_category determinism gap**: amendment EXPLICITLY DOCUMENTS the gap as a Phase 4e launch-blocking candidate. Pre-launch posture means no past periods exist that could expose the gap; the role-override + dated-tri-state lookup ships in S42 with the gap documented in `bug_correction_history.action: provisional-pending-phase-4e-employment-category-dating`. Phase 4e candidate sprint adds dated `employment_category` via either (a) extending `employee_profiles` with a versioned `employment_category` column, or (b) introducing a new `user_employment_categories` versioned-config table mirroring the S34 `user_agreement_codes` shape. Choice between (a) and (b) is itself a Phase 4e architectural decision; amendment defers.

### Phase 2 — Step 7a-equivalent Dual-Lens (TASK-41A-02)

**MANDATORY** per `sprint-close-guard.ps1` hook. Codex + Reviewer Agent in parallel against S41a diff. Cycle-cap 2 per lens.

Review focus: does the 4-seam amendment cleanly settle the cycle-1 BLOCKERs without introducing new architectural ambiguities? Specifically: (1) Does Seam A boolean-disabler-via-merge actually work for NONE + DISCRETIONARY both, or is DISCRETIONARY a separate semantic? (2) Does Seam B PCS-not-BuildLine seam preserve atomic-tx contract? (3) Does Seam C new-overload signature handle all existing callers cleanly? (4) Does Seam D defer-to-Phase-4e introduce any pre-launch correctness risk?

### Phase 3 — Sprint Close (TASK-41A-03)

Close sections + INDEX + ROADMAP entry + MEMORY. Sprint-close commit through hook.

## Forward Pointers

- **S42 = ADR-024 D1+D2 cutover** (was S41 pre-amendment): re-draft refinement against the amended ADR; implement rule-engine + payroll + admin endpoint + frontend per the 4 settled seams.
- **S43 = ADR-024 Sub-Sprint 2b**: D7 necessity-ack + Bug #4 HK/PROSA seed flip + D6 generalized correct-as-bug endpoint.
- **S44 = ADR-024 Sub-Sprint 3**: D-test matrix + Phase E completion.
- **Phase 4e candidate (launch-blocking)**: employment_category dating (Seam D resolution).
