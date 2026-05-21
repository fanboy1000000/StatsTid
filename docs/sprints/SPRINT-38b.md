# Sprint 38b — ADR-026 Authorship (Audit Visibility Surface — Path C Event-Projection)

| Field | Value |
|-------|-------|
| **Sprint** | 38b |
| **Status** | **complete** |
| **Start Date** | 2026-05-21 |
| **End Date** | 2026-05-21 |
| **Orchestrator Approved** | yes — 2026-05-21 |
| **Build Verified** | N/A — design-only sprint; no code changes |
| **Test Verified** | N/A — test totals unchanged from S38 (869 total) |
| **Sprint-start commit base** | `92cfbd6` (S38 close polish, 2026-05-21) |
| **Sprint-end HEAD** | _filled by close commit_ |
| **Sprint type** | **DESIGN-ONLY** — produces ADR-026 settling the S38 D7 deferral. Path (C) event-projection chosen per user adjudication. |
| **Plan** | `.claude/plans/PLAN-s38b.md` |
| **Phase** | 4e (Phase C continuation) |

## Sprint Goal

Author `docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md` from PLANNED placeholder to ACCEPTED design. Path (C) event-projection chosen — leverage ADR-018 D13 sync-in-tx pattern; new `audit_projection` table with explicit `target_org_id` columns; per-event projection declarations; event log stays immutable per ADR-001.

## Why Path C (vs A/B/D)

- **(A) scope-by-actor** rejected — operator/system actions invisible to tenant; explicit launch concern per cycle-3 review trail
- **(B) audit_log schema extension** rejected — middleware retrofit touching every state-changing endpoint with per-endpoint target_org_id declarations; out of proportion for the result
- **(C) event-projection per ADR-018 D13** ← CHOSEN — aligns with S27 ProjectionBackfillService established posture; per-event explicit declarations cleaner than per-endpoint middleware; preserves event-log immutability per ADR-001
- **(D) hybrid** rejected — unnecessary complexity; C alone sufficient

## Step 7a Dual-Lens (TASK-38B-02)

**MANDATORY per `sprint-close-guard.ps1` hook**. Codex + Reviewer Agent in parallel against S38b diff. Cycle-cap 2 per lens.

Review focus per S38 lessons: does the path (C) design substantively close the cycle-1/cycle-2/cycle-3 concerns from the prior D7 attempt, or does it surface new defects in the same architectural area? If the latter — the audit-visibility surface is wider than even ADR-026 can carry and needs further decomposition.

## Test Summary

`sprint-test-validation` SKIP — design-only contract; test totals unchanged at 869.

## Forward Pointers

- **S39 schema migration** — adds `audit_projection` table + indexes to the 6 ADR-024+ADR-025 schema entries; ADR-026 backfill seeder runs as part of greenfield migration
- **S40 cutover** — implements `GET /api/admin/audit` endpoint + `AuditProjectionRepository` + `AuditLogView.tsx` admin UI + per-event projection mappings for ADR-024's 7 events + ADR-025's 4 events + select pre-existing audit-relevant events
- **S41 D-tests** — cross-tenant audit leakage + projection backfill idempotency + event-coverage Phase E test

---

## Sprint Close (TASK-38B-03)

### Outcome

**ADR-026 ACCEPTED** (path C event-projection per ADR-018 D13). 3-cycle Step 7a dual-lens trail to lens convergence; halt-and-prompt did NOT fire. ADR-025 D7 SUPERSEDED-BY-ADR-026.

### Step 7a Dual-Lens Trail (3 cycles to convergence)

| Cycle | Codex | Reviewer | Absorption commit |
|-------|-------|----------|-------------------|
| 1 | BLOCKED 3 P1 (B1 dispatch / B2 event names / B3 NULL-target overload) | APPROVED-WITH-WARNINGS (W1+W2+W3 convergent on B1+B3) | `ceda80d` (D2 endpoint-direct reframe + B2 canonical event names + B3 3-tier `visibility_scope` enum + CHECK constraint + per-class D-tests; mapper count ~24→~34) |
| 2 | BLOCKED (NEW-B1 Consequences L336 ghost = same-edit partial absorption from cycle 1) | BLOCKED (NEW-B1 + W-new-1 = mapper count ~34 vs actual ~53); flagged as missed-facts-in-same-edit per `feedback_missed_facts_vs_thrash.md`, recommended cycle-3 textual fix not halt-and-prompt | `1b374e6` (L336 rewritten with explicit "NO event-handler bus" + mapper count fully recounted to ~53) |
| 3 | **APPROVED** (NEW-B1 + W-new-1 CLOSED; no new defects) | **APPROVED** (verified mapper math: 42 retrofit + 11 new = 53) | — (ADR flips DRAFT → ACCEPTED) |

### Governance Validation

**First session application of post-S35 governance commit `a094630`** ("cap fires AFTER verification IF cycle 3 surfaces new BLOCKERs"). Cycle-3 verification clean → halt-and-prompt does NOT fire → ADR flips ACCEPTED. The extended cycle-cap-by-1 mechanism worked as designed.

### Commit List (5 commits)

```
6fd0800 S38b TASK-38B-00: sprint open
1b312c4 S38b TASK-38B-01: ADR-026 DRAFT (path C event-projection)
ceda80d S38b TASK-38B-02 cycle 1 absorption: 3 Codex BLOCKERs + Reviewer convergent + mapper count
1b374e6 S38b TASK-38B-02 cycle 2 absorption: Consequences L336 ghost + mapper recount
[this commit] S38b TASK-38B-03: sprint close
```

### Forward Pointers

- **S39 schema migration** — adds `audit_projection` table + 3 indexes (per ADR-026 D1) to the 6 ADR-024+ADR-025 ledger entries; backfill seeder runs as part of greenfield migration
- **S40 cutover** — ADR-026 implementation: `AuditProjectionRepository` + `IAuditProjectionMapper<T>` interface + ~53 mappers + `OrgScopeValidator.GetAccessibleOrgsAsync` commissioned helper + `GET /api/admin/audit` endpoint + `AuditLogView.tsx` admin page + `AuditProjectionBackfillService` + `audit-projection-catalog.md` doc
- **S41 D-tests** — 5 D-tests per ADR-026 D7: event-coverage invariant + backfill idempotency + sync-in-tx RYW + per-class visibility (3 sub-tests for TENANT_TARGETED / GLOBAL_TENANT_VISIBLE / GLOBAL_ADMIN_ONLY) + schema CHECK enforcement negative test
- **Customer-go-live commitment** per PROGRAM L279 + ADR-025 §Customer-go-live: now UNBLOCKED architecturally pending S39+S40 implementation

### Test Summary

`sprint-test-validation` SKIP — design-only contract; test totals unchanged at 869.
