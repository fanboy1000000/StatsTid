# PLAN — Sprint 42a: ADR-024 2nd Amendment (Cross-Process Boundary + Tx Envelope)

| Field | Value |
|-------|-------|
| **Sprint** | 42a (sub-sprint mirroring S41a precedent — 2nd ADR-024 amendment) |
| **Phase** | 4e (Phase D ADR-024 2nd-cycle amendment) |
| **Sprint type** | **DESIGN-ONLY** — 2nd amendment to ADR-024 settling 3 new seams that S42 refinement cycle 1 surfaced as load-bearing BLOCKERs (Codex caught a 5th architectural seam the S41a amendment didn't cover). No code changes. |
| **Base commit** | `6dc9008` (S41a close) |
| **Predecessor refinement** | `REFINEMENT-s42-d1d2-pre-2nd-amendment-cycle-trail.md` (S42 attempt; surfaced the 3 new seams but didn't settle them) |
| **Sprint open date** | 2026-05-23 |
| **Projected end date** | 2026-05-23 (single-day) |
| **Task count** | 4 (TASK-42A-00..03) |

## Sprint Goal

Author a `## Amendment 2026-05-23 — Cross-Process Boundary + Tx Envelope (S42a)` section appended to `docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md` (after the existing S41a amendment) settling 3 new seams S42 refinement cycle 1 surfaced. **Implementation refinement re-drafts against the doubly-amended ADR at S43 sprint open.**

## The 3 New Seams (per S42 cycle 1 dual-lens findings)

S41a settled 4 seams (A/B/C/D). S42 refinement cycle 1 surfaced 3 NEW seams the prior amendment didn't cover:

1. **Seam E — Rule-engine HTTP boundary**: PCS sends `ruleId/profile/entries/periodStart/periodEnd` to `/api/rules/evaluate`; `EvaluateRequest` has NO config field; `RuleRegistry` loads STATIC config via `AgreementConfigProvider.GetConfig(...)`. The S41a Seam A merge in Backend.Api therefore NEVER reaches OvertimeRule's execution context.
2. **Seam F — Tx envelope vs EmitManifestAsync degraded-audit pattern**: `EmitManifestAsync` deliberately opens its own connection + uses two-independent-try/catch degraded-audit semantic. S41a Seam B's "wrap segment loop + EmitManifestAsync in single tx" breaks the degraded-audit recovery property.
3. **Seam G — Audit-line entry contract**: S41a amendment L344-345 mandate `DISCRETIONARY_PENDING_ADMIN` + `NONE_NO_ENTITLEMENT` entries but didn't specify WHERE they live.

## Phase Decomposition

Orchestrator-direct sequential per S41a precedent.

| Phase | Tasks | Dispatch |
|-------|-------|----------|
| 0 | TASK-42A-00 | Sprint open |
| 1 | TASK-42A-01 | ADR-024 2nd amendment authorship |
| 2 | TASK-42A-02 | Step 7a-equivalent dual-lens review |
| 3 | TASK-42A-03 | Sprint close |

## Proposed Seam Decisions (TASK-42A-01 substance)

**Seam E**: extend `EvaluateRequest` HTTP contract with optional `MergedConfig: AgreementRuleConfig?` field. Supplied → RuleRegistry uses it; null → falls back to existing `AgreementConfigProvider.GetConfig(...)` for backward compat. Backend.Api PCS computes merge via ConfigResolutionService dated overload and passes merged config in HTTP body. Keeps RoleConfigOverrideRepository DI in Backend/Payroll only; RuleEngine.Api stays as stateless calculator. Alternative rejected: move RoleConfigOverrideRepository to RuleEngine.Api project (more invasive + violates Backend-computes-RuleEngine-executes division S20 established).

**Seam F**: keep EmitManifestAsync self-tx-managed (preserves degraded-audit recovery per ADR-016 D10). PCS opens SEPARATE tx solely for DISCRETIONARY event emission — one tx per segment emitting a DISCRETIONARY event. Manifest emit + DISCRETIONARY emits in separate atomic boundaries. Loses: cross-manifest-and-discretionary atomicity (manifest could succeed degraded while DISCRETIONARY rolls back). Gains: degraded-audit semantic preserved.

**Seam G**: NEW dedicated audit table `compensation_choice_audit` with columns `(audit_id BIGSERIAL PK, employee_id TEXT, date DATE, segment_id UUID NULL, employment_category TEXT, compensation_choice TEXT CHECK IN ('CONTRACTUAL_NORMAL', 'DISCRETIONARY_PENDING_ADMIN', 'NONE_NO_ENTITLEMENT'), merarbejde_hours NUMERIC(7,2) NULL, manifest_id UUID NULL REFERENCES segment_manifests, recorded_at TIMESTAMPTZ DEFAULT NOW())`. Written by PCS in same Seam F tx as DISCRETIONARY event. Schema migration ships in S43 as new ledger entry `s43-d2-compensation-choice-audit`. Alternative rejected: extending PayrollExportLine with compensation_choice column (pollutes line-item contract with role-stratum metadata).

## Architectural Constraints

_Checked at close._

- [ ] **P1** — cross-process boundary settlement (Seam E) is load-bearing
- [ ] **P3** — Seam F honors ADR-018 D3 within the separate-tx scope; acknowledges degraded-audit pattern as load-bearing per ADR-016 D10
- [ ] **P5** — Seam E doesn't shift role_config_overrides DI; preserves Backend/RuleEngine division

## Forward Pointers

- **S43 = ADR-024 D1+D2 cutover** (re-drafted against doubly-amended ADR): ~9-11 tasks given new Seam E (EvaluateRequest extension + RuleRegistry conditional consumption) + Seam G (compensation_choice_audit table + write site)
- **S44 = ADR-024 D7+D6+Bug #4** (was S43)
- **S45 = ADR-024 Sub-Sprint 3** (D-test matrix + Phase E; was S44)
- **Phase 4e launch-blocking candidate** = employment_category dating (Seam D unchanged)

## Cycle-trail discipline observation

This is the 6th sprint slot on ADR-024 work (S38 design + S40 schema + S41 abandoned + S41a 1st amendment + S42 abandoned + S42a 2nd amendment). **If S43 refinement cycle 1 surfaces ANOTHER architectural seam, that's cycle 7 same-area thrash — the discipline says ROLL BACK ADR-024 D1+D2 cutover entirely as under-specified-for-current-architecture.** User-adjudication call at that point.
