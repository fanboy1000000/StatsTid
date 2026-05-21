# PLAN — Sprint 38: Phase C Design Sprint — ADR-024 + ADR-025 + ADR-013 Amendment

## Sprint Header

| Field | Value |
|-------|-------|
| **Sprint** | 38 |
| **Title** | Phase C Design Sprint (ADR-024 role-within-agreement + ADR-025 multi-tenant operational concerns + ADR-013 amendment) |
| **Status** | DRAFT |
| **Start Date** | 2026-05-21 |
| **Projected End Date** | 2026-05-21 (single-day; design-only sprint per S28 / S32 precedent) |
| **Sprint-start commit base** | `7b12ca1` (S37 close polish, 2026-05-21) |
| **Sprint type** | **DESIGN-ONLY** — produces ADR-024 + ADR-025 + ADR-013 amendment. No code changes; no schema changes; no test changes. Mirrors S28 (ADR-020) + S32 (ADR-023) design-only pattern. |
| **Refinement** | Not filed. Design scope settled via PROGRAM-s36-s41 + S37 S37 absorption findings + ADR-024 D7 added 2026-05-21. |
| **Plan** | this file |
| **Phase** | 4e (Phase C ADR authorship per PROGRAM L121–164) |

## Sprint Goal

Produce three KB entries that finalize the domain-correctness program's architectural decisions:

- **ADR-024 — Role-Within-Agreement Modeling + Correction Policy + Classification Governance**
  - D1: Role dimension placement (3 options: extend PositionOverrideConfigs / activate EmploymentCategory + RoleConfigOverride / promote senior roles to separate agreement codes)
  - D2: Compensation entitlement model (tri-state MerarbejdeCompensationRight: CONTRACTUAL / DISCRETIONARY / NONE)
  - D3: Correction policy formalization (codify ROADMAP rule correction policy + binary classification framework)
  - D4: Classification governance (who classifies; workflow for surfacing + adjudicating)
  - D5: Interpretation authority (Personalestyrelsen default + per-cell deviation documentation)
  - D6: Bug correction operational model (operator-triggered; no per-institution opt-in; new event type)
  - **D7** (added S37 TASK-3704): Overtime authorization model — pre-approval + post-hoc necessity-acknowledgment workflow (scope: schema + endpoint + UI + audit-trail; lands S40 jointly with HK/PROSA seed flip; payroll-mapping replay-determinism implication per Reviewer N2)
  - **D8 candidate** (added S37 TASK-3707): bug_correction_history schema rev — either merge into D3 (correction policy formalization) or promote as standalone D8

- **ADR-025 — Multi-Tenant Operational Concerns**
  - D1: Per-tenant SLS payroll endpoint
  - D2: Customer-onboarding workflow
  - D3: GDPR per-tenant export + Article 17 right-to-erasure
  - D4: Noisy-neighbor / per-tenant fairness
  - D5: SaaS-operator cross-tenant reporting
  - D6: Per-tenant feature flags
  - D7: Tenant-scoped audit visibility
  - D8: Explicit `Institution` type vs generic top-level org

- **ADR-013 amendment** — cross-reference ADR-024 D3 + D6: bug corrections become an explicit-cascade trigger under ADR-013's no-cascade discipline.

## Interim-Expert Posture (carry-over from S37)

External Phase B engagement remains pending. S38 design proceeds without expert-cite verification on the underlying cirkulær claims (e.g., AC chefkonsulent's no-merarbejde-entitlement is treated as structural concern, not confirmed cirkulær-paragraph fact). Implications:

- ADR-024 D1 architectural choice (3 options) is made on the design-correctness frame (which option is cleanest for the system architecture), not on confirmed cirkulær wording.
- ADR-024 D2 tri-state model design is made on the structural-need frame.
- The chefkonsulent-merarbejde gap continues to be flagged "PROVISIONAL pending Phase B" in `role-dimension-audit.md` — the ADR design accommodates whichever Phase B confirms.
- When real Phase B engages, the ADRs may need amendment if expert findings diverge from S37 interim interpretations.

This is the design-without-expert posture the user explicitly accepted at S38 open ("we don't have a domain expert atm"). Honest scoping in the ADR text.

## Phase Decomposition

Mirrors S28 / S32 design-only precedent — Orchestrator-direct sequential, no agent dispatches.

| Phase | Tasks | Dispatch |
|-------|-------|----------|
| 0 | TASK-3800 | Sprint open (this file + SPRINT-38 + INDEX) |
| 1 | TASK-3801 | ADR-024 authorship (Orchestrator-direct per WORKFLOW.md L48 — KB writes are Orchestrator-only) |
| 2 | TASK-3802 | ADR-025 authorship |
| 3 | TASK-3803 | ADR-013 amendment |
| 4 | TASK-3804 | Step 7a-equivalent dual-lens review (Codex external + Reviewer Agent in parallel; cycle-cap 2 per lens; **MANDATORY per sprint-close-guard.ps1 hook**) |
| 5 | TASK-3805 | Sprint close (gated by hook + Step 7a artifacts with verdict lines) |

## Step 0a — Entropy Scan

| Check | Result |
|-------|--------|
| KB path validation | CLEAN |
| ADR numbering | CLEAN — ADR-024 / 025 / 013 amendment do not collide with existing ADRs (last filed ADR-023 in S32) |
| Mechanical gate | ACTIVE per `297fdee`; S38 close commit gated by hook |
| PROGRAM consistency | ADR-024 expanded to 7+1 decisions per S37 absorption commits (`fa00d97` added D7; `e4c6517` added D8 candidate framing in source register schema row) |

## Step 0b — Plan Review

**SKIP** — design-only sprint per S28 / S32 precedent. Step 7a-equivalent at close is the formal review gate (now mechanically enforced).

## Architectural Constraints

_Checked at close._

- [ ] **P1 — Architectural integrity** → New ADRs file at `docs/knowledge-base/decisions/`; KB INDEX updated; cross-references to prior ADRs (013, 014, 016, 017, 018, 019, 020, 021, 022, 023) preserved.
- [ ] **P2 — Rule engine determinism** → ADR-024 D1 + D2 design must preserve PCS / EmploymentProfileResolver replay-determinism per ADR-016 D10. Specifically D1 option (a) extend-PositionOverrideConfigs has zero impact; (b) activate-EmploymentCategory + RoleConfigOverride needs a new dated-lookup path mirroring S33 ADR-023 D1 pattern; (c) promote-to-separate-agreement-codes works with existing PCS path.
- [ ] **P3 — Event sourcing / auditability** → ADR-024 D6 + D7 new event types must follow PAT-004 event registration; ADR-013 amendment makes bug-correction explicit cascade trigger.
- [ ] **P4 — Version correctness** → ADR-024 D1 model must accommodate OK24 / OK26 + future OK28 transitions.
- [ ] **P6 — Payroll integration correctness** → ADR-024 D2 tri-state model + D7 workflow extension are payroll-correctness foundations.
- [ ] **P7 — Security / access control** → ADR-025 D5 (cross-tenant reporting), D7 (audit visibility), D8 (Institution type) touch security model.

## Task Log

6 declared tasks (TASK-3800..3805). Plan file is source-of-truth for per-task detail.

### Phase 0 — Sprint Open

#### TASK-3800 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3800 |
| **Status** | in-progress |
| **Components** | `.claude/plans/PLAN-s38.md`, `docs/sprints/SPRINT-38.md`, `docs/sprints/INDEX.md` |
| **Dependencies** | none |

### Phase 1 — ADR-024 (TASK-3801)

**Scope**: draft `docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md` settling D1-D7 (+ D8 merge-or-promote). 

**Per-decision authoring order** (within ADR text):
1. D1: role dimension placement (architectural foundation — 3 options enumerated; pick one with rationale)
2. D2: tri-state MerarbejdeCompensationRight (depends on D1 choice for placement)
3. D3: codify correction policy + binary classification framework (formalizes ROADMAP commitment)
4. D4: classification governance workflow (encoding owner + product owner review)
5. D5: interpretation authority (Personalestyrelsen default + per-cell deviation)
6. D6: bug correction operational model (operator-triggered + global uniform; new `AgreementConfigBugCorrected` event type)
7. D7: overtime authorization model (pre-approval + post-hoc necessity-acknowledgment workflow; schema + endpoint + UI + audit-trail; payroll-mapping replay-determinism scope)
8. D8 decision: merge into D3 (correction policy → schema row) OR promote as standalone D8 (formal source-register schema definition)

User adjudication required on the D1 fork (the 3 options have meaningful architectural implications). Other decisions can ride on default-recommendations per the design scope already documented in PROGRAM L125-136 + S37 D7 addition.

### Phase 2 — ADR-025 (TASK-3802)

**Scope**: draft `docs/knowledge-base/decisions/ADR-025-multi-tenant-operational-concerns.md` settling D1-D8. These are operational/SaaS-architecture decisions (per-tenant SLS endpoint, customer-onboarding workflow, GDPR per-tenant erasure, noisy-neighbor fairness, cross-tenant reporting, feature flags, audit visibility, Institution type) — orthogonal to ADR-024.

Each D1-D8 has more design latitude than ADR-024 D1; defaults per PROGRAM L142-151 + light user adjudication on any forks.

### Phase 3 — ADR-013 Amendment (TASK-3803)

**Scope**: amend `docs/knowledge-base/decisions/ADR-013-no-cascade.md` (or its current filename) to cross-reference ADR-024 D3 + D6. Bug corrections become an explicit-cascade trigger under ADR-013's no-cascade discipline. The cascade is explicit (operator-triggered) not implicit (rule-engine-derived).

Small textual addition; no Status field change (stays ACCEPTED).

### Phase 4 — Step 7a Dual-Lens (TASK-3804)

**Scope**: Codex external (read-only) + Reviewer Agent in parallel against the S38 diff (`7b12ca1..HEAD`). Cycle-cap 2 per lens. Save artifacts at `.claude/reviews/SPRINT-38-step7a-{codex,reviewer}.md` with verdict lines. Mandatory per the new sprint-close-guard.ps1 hook.

**Review focus for ADRs** (different from implementation-sprint Step 7a):
- Decision rationale completeness (does each D-decision enumerate the alternatives + state why one was chosen?)
- Cross-ADR consistency (do ADR-024 + ADR-025 + amended ADR-013 mutually reinforce?)
- Forward-compatibility with PROGRAM enumeration (S39 schema + S40 cutover + S41 D-tests should derive cleanly)
- Interim-expert posture honesty (ADR text acknowledges the Phase-B-pending state without overcommitting)

### Phase 5 — Sprint Close (TASK-3805)

**Scope**: SPRINT-38.md close sections + INDEX.md finalization + ROADMAP Phase 4e annotation + MEMORY.md S38 line. Sprint-close commit through the gate.

## Forward Pointers

- **S39 schema migration** — drives off ADR-024 D1 choice (table + columns differ per option a/b/c) + ADR-024 D7 (overtime workflow extension schema)
- **S40 cutover** — implements ADR-024 D1 + D2 + D7; lands HK/PROSA OvertimeRequiresPreApproval seed flip jointly with the workflow extension
- **S41 D-test matrix + governance bake-in** — exhaustive testing of the cutover + danish-agreements.md rewrite + WORKFLOW.md OK-version transition checklist + QUALITY.md re-grade
- **Real Phase B engagement** (still pending) — when it lands, may trigger ADR-024 amendments if expert findings diverge from S37 + S38 interim interpretations
