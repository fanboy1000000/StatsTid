# Sprint 38 — Phase C Design Sprint (ADR-024 + ADR-025 + ADR-013 Amendment)

| Field | Value |
|-------|-------|
| **Sprint** | 38 |
| **Status** | **complete (with D7 deferred to ADR-026 per cycle-3 halt-and-prompt)** |
| **Start Date** | 2026-05-21 |
| **End Date** | 2026-05-21 |
| **Orchestrator Approved** | yes — 2026-05-21 |
| **Build Verified** | N/A — design-only sprint; no code changes |
| **Test Verified** | N/A — test totals unchanged from S37 (869 total). `sprint-test-validation` SKIP at close per design-only contract. |
| **Sprint-start commit base** | `7b12ca1` (S37 close polish, 2026-05-21) |
| **Sprint-end HEAD** | `964a872` (TASK-3805 sprint close) |
| **Sprint type** | **DESIGN-ONLY** — produces ADR-024 + ADR-025 + ADR-013 amendment. Mirrors S28 (ADR-020) + S32 (ADR-023) design-only pattern. |
| **Plan** | `.claude/plans/PLAN-s38.md` |
| **Phase** | 4e (Phase C ADR authorship per PROGRAM L121–164) |

## Sprint Goal

Produce the architectural foundations for the S39–S41 implementation pipeline:

- **ADR-024 — Role-Within-Agreement Modeling + Correction Policy + Classification Governance** (7 confirmed decisions + 1 candidate to merge or promote: D1 role dimension placement / D2 tri-state MerarbejdeCompensationRight / D3 correction policy formalization / D4 classification governance / D5 interpretation authority / D6 bug correction operational model / D7 overtime authorization model added S37 / D8 candidate bug_correction_history schema rev added S37)
- **ADR-025 — Multi-Tenant Operational Concerns** (8 decisions: D1 per-tenant SLS endpoint / D2 customer-onboarding workflow / D3 GDPR per-tenant erasure / D4 noisy-neighbor fairness / D5 cross-tenant reporting / D6 per-tenant feature flags / D7 audit visibility / D8 Institution type)
- **ADR-013 amendment** — cross-reference ADR-024 D3 + D6: bug corrections become an explicit-cascade trigger under no-cascade discipline.

## Interim-Expert Posture (carry-over from S37)

External Phase B engagement remains pending. S38 design proceeds **without expert-cite verification** on underlying cirkulær claims. The chefkonsulent-merarbejde gap (ADR-024 D1 + D2 motivation) continues to be framed as a structural concern per `role-dimension-audit.md` PROVISIONAL labeling, not a confirmed cirkulær-paragraph fact.

**Implications for the ADR text**:
- ADR-024 D1 architectural choice (3 options: extend `PositionOverrideConfigs` / activate `EmploymentCategory` + `RoleConfigOverride` / promote senior roles to separate agreement codes) is decided on **system-design correctness** (which option is cleanest architecturally), not on confirmed cirkulær wording.
- ADR-024 D2 tri-state model is justified on **structural need** (binary `DefaultCompensationModel` + `EmployeeCompensationChoice` can't express "no contractual right").
- When real Phase B engages, ADRs may need amendment if expert findings diverge.

Honest scoping in the ADR text — each decision explicitly notes whether it depends on Phase B confirmation or stands on architecture alone.

## Plan Review (Step 0b)

**SKIP** — design-only sprint per S28 / S32 precedent. Step 7a-equivalent at close is the formal review gate (now **mechanically enforced** by `sprint-close-guard.ps1` hook from session governance fix).

## Architectural Constraints

_Checked off at sprint close._

- [ ] **P1 — Architectural integrity** → 3 new/amended KB entries; cross-references to prior ADRs preserved
- [ ] **P2 — Rule engine determinism** → ADR-024 D1 + D2 design preserves PCS / EmploymentProfileResolver replay-determinism per ADR-016 D10
- [ ] **P3 — Event sourcing / auditability** → ADR-024 D6 + D7 new event types follow PAT-004; ADR-013 amendment makes bug-correction explicit cascade trigger
- [ ] **P4 — Version correctness** → ADR-024 model accommodates OK24 / OK26 / future OK28
- [ ] **P6 — Payroll integration correctness** → ADR-024 D2 tri-state + D7 workflow are payroll-correctness foundations
- [ ] **P7 — Security / access control** → ADR-025 D5 / D7 / D8 touch security model

## Task Log

6 declared tasks (TASK-3800..3805) across 6 phases. Plan file `.claude/plans/PLAN-s38.md` is source-of-truth.

### Phase 0 — Sprint Open

#### TASK-3800 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3800 |
| **Status** | in-progress |
| **Components** | `.claude/plans/PLAN-s38.md`, `docs/sprints/SPRINT-38.md`, `docs/sprints/INDEX.md` |

### Phase 1 — ADR-024 Authorship (TASK-3801)

Draft `docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md` settling D1-D7 + D8 merge/promote. User adjudication required on the D1 fork (3 architecturally meaningful options).

### Phase 2 — ADR-025 Authorship (TASK-3802)

Draft `docs/knowledge-base/decisions/ADR-025-multi-tenant-operational-concerns.md` settling D1-D8 (operational / SaaS-architecture decisions; lighter user adjudication needed).

### Phase 3 — ADR-013 Amendment (TASK-3803)

Small textual addition cross-referencing ADR-024 D3 + D6. No Status change.

### Phase 4 — Step 7a Dual-Lens (TASK-3804)

**MANDATORY per `sprint-close-guard.ps1` hook**. Codex external + Reviewer Agent in parallel against S38 diff. Cycle-cap 2 per lens. Artifacts at `.claude/reviews/SPRINT-38-step7a-{codex,reviewer}.md` with verdict lines.

Review focus: decision rationale completeness, cross-ADR consistency, forward-compatibility with PROGRAM enumeration, interim-expert posture honesty in ADR text.

### Phase 5 — Sprint Close (TASK-3805)

Close sections + INDEX + ROADMAP + MEMORY. Sprint-close commit through the gate.

## External Review (Step 7a-equivalent)

| Field | Value |
|-------|-------|
| **Invoked** | at TASK-3804, mandatory per hook |
| **Sprint diff range** | `7b12ca1..HEAD` |
| **Cycle-cap** | 2 per lens |

## Test Summary

`sprint-test-validation` SKIP — design-only contract; no test code touched; test totals unchanged at 869.

## Forward Pointers

- **S39 schema migration** — drives off ADR-024 D1 choice (table + columns differ per option a/b/c) + ADR-024 D7 (overtime workflow extension schema)
- **S40 cutover** — implements ADR-024 D1 + D2 + D7; lands HK/PROSA OvertimeRequiresPreApproval seed flip jointly with the workflow extension
- **S41 D-test matrix + governance bake-in** — exhaustive testing + danish-agreements.md rewrite + WORKFLOW.md OK-version transition checklist + QUALITY.md re-grade
- **Real Phase B engagement** (still pending) — when it lands, may trigger ADR amendments if expert findings diverge from S37 + S38 interim interpretations

---

## Sprint Close (TASK-3805)

### Outcomes Summary

3 KB entries landed ACCEPTED + 1 placeholder filed under user-authorized defer per halt-and-prompt discipline.

| ADR | Status | Decisions |
|-----|--------|-----------|
| ADR-024 | ACCEPTED | 7 of 7 decisions clean (D1-D7; D8 candidate merged into D3 per user adjudication 2026-05-21) |
| ADR-025 | ACCEPTED-WITH-D7-DEFERRED | 7 of 8 decisions clean (D1-D6 + D8). D7 deferred to ADR-026. |
| ADR-013 amendment | ACCEPTED (no Status bump) | Cross-references ADR-024 D3 + D6 + ADR-027 (post-launch) + ADR-016 D10 |
| ADR-026 | PLANNED (placeholder) | Audit visibility surface — open problem + 4 architectural paths + 5 design-sprint questions; cannot defer past S39 per launch commitment |

### Step 7a Dual-Lens Trail (TASK-3804)

**Three cycles** — design-sprint cycle-cap 2 exceeded by 1 per halt-and-prompt extension:

| Cycle | Codex | Reviewer | Absorption commit |
|-------|-------|----------|-------------------|
| 1 | BLOCKED (3 P1: D6 single-surface + D7 nonexistent code + D6 feature-flag glocal) | APPROVED-WITH-WARNINGS (W2 convergent on overtime feature flags) | `c59b768` cycle-1 absorption |
| 2 | BLOCKED (P1.NEW-1 event-name propagation + P1.NEW-2 D7 row-shape gap, same areas) | BLOCKED (B1+B2 D7 citation defects, same area) | `e91fe82` cycle-2 absorption (D7 substantively reframed; ADR-013 + ADR-024 References updated for renamed event; count fixed) |
| 3 | BLOCKED (P1.C3-1 D7 internal contradiction, same area) | APPROVED (all cycle-2 BLOCKERs CLOSED from Reviewer lens) | `dadc1b4` halt-and-prompt resolution (D7 stripped + ADR-026 filed + statuses flipped) |

Cycle-3 lens divergence widening + 3rd cycle finding new defects in same D7 area = canonical halt-and-prompt signal per `feedback_thrash_defer_real_world.md`. User adjudicated defer.

### Halt-and-Prompt Resolution

**First session application of the halt-and-prompt + user-authorized defer flow.** The discipline worked as designed:

- Reviewer's lens caught literal text/citation defects + verified mechanical absorption.
- Codex's lens caught semantic-consistency defects across absorbed text — including the cycle-3 internal contradiction Reviewer didn't surface.
- Both lens findings informed the user's defer decision.
- The contested area moved to a dedicated venue (ADR-026) with full design latitude preserved.
- The non-contested 14 of 15 decisions ship clean.

### Commit List (8 commits across S38)

```
09fd468 S38 TASK-3800: sprint open
95e9896 S38 TASK-3801: ADR-024 DRAFT
bf78da6 S38 TASK-3802: ADR-025 DRAFT
41d4fd2 S38 TASK-3803: ADR-013 amendment
c59b768 S38 TASK-3804 cycle 1 absorption: 3 Codex BLOCKERs + 1 Reviewer convergent + cosmetics
e91fe82 S38 TASK-3804 cycle 2 absorption: event-name propagation + D7 audit-design substantive reframe + mechanical citation fixes
dadc1b4 S38 TASK-3804 cycle 3 halt-and-prompt resolution: defer D7 audit-visibility to dedicated ADR-026
[this commit] S38 TASK-3805: sprint close
```

### Forward Pointers

- **ADR-026** — audit visibility surface design sprint. Sprint slot TBD. CANNOT defer past S39 per launch commitment.
- **S39 schema migration** — 6 ledger entries: role_config_overrides (ADR-024 D1) + overtime_pre_approvals extension (ADR-024 D7) + tenant_sls_configurations (ADR-025 D1) + erased_users_audit (ADR-025 D3) + local_configurations.feature_flags ALTER (ADR-025 D6) + institutions table (ADR-025 D8). If ADR-026 schema-extension path chosen, +1-2 entries for audit_log columns.
- **S40 cutover** — implements ADR-024 D1+D2+D6+D7 + ADR-025 D1+D2+D3+D5+D6+D8 + Bug #4 HK/PROSA seed flip jointly with ADR-024 D7 workflow.
- **S41 D-test matrix + governance bake-in** — exhaustive D-tests per accepted ADR + ADR-026's tests (whenever it lands).

### Phase B Engagement Status (unchanged from S37)

Still pending. ADR-024 decisions are PROVISIONAL pending real Phase B paragraph cites where flagged per-decision; architecture stands regardless.

### Governance Lessons (candidates for feedback memory)

- **Halt-and-prompt works when both lenses are running.** Reviewer APPROVED + Codex BLOCKED at cycle 3 is exactly the divergence signal that single-lens review would miss.
- **Defer is the right tool when an architectural area discloses new defects across 3+ cycles.** The audit-visibility surface needed its own design slot; carrying it inside ADR-025 as one decision among 8 was the wrong scope.
- **The mechanical hook + cycle-cap discipline produced this clean resolution.** First session sprint-close exercising the cycle-3-defer pattern; pattern is now battle-tested.
