# PLAN — Sprint 38b: ADR-026 Authorship (Audit Visibility Surface — Path C Event-Projection)

## Sprint Header

| Field | Value |
|-------|-------|
| **Sprint** | 38b (sub-sprint splitting off the S38 D7 deferral; numbered sub-sprint to preserve S39 main-sequence numbering) |
| **Title** | Audit Visibility Surface — ADR-026 authorship (Path C event-projection per ADR-018 D13) |
| **Status** | DRAFT |
| **Start Date** | 2026-05-21 |
| **Projected End Date** | 2026-05-21 (single-day; design-only single-ADR sprint) |
| **Sprint-start commit base** | `92cfbd6` (S38 close polish, 2026-05-21) |
| **Sprint type** | **DESIGN-ONLY** — produces ADR-026 settling the deferred ADR-025 D7 design. Mirrors S38 single-ADR-sprint pattern. |
| **Plan** | this file |
| **Phase** | 4e (Phase C continuation per S38 cycle-3 halt-and-prompt resolution) |

## Sprint Goal

Author `docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md` (currently PLANNED placeholder) into ACCEPTED state with **path (C) event-projection per ADR-018 D13** chosen per user adjudication 2026-05-21.

**Path C design scope**:

1. New `audit_projection` table (post-S27 sync-in-tx projection pattern) with explicit `event_id` + `target_org_id` + `target_resource_id` + `event_type` + `timestamp` + actor metadata + payload-derived details columns
2. Per-event projection declaration mechanism (each audit-relevant event type declares its projection mapping)
3. Audit-relevant event inventory: all 11 new events from S38 (ADR-024's 7 + ADR-025's 4) + audit-on-write retrofit for select pre-existing state-changing events
4. Tenant-scoped admin query surface: `GET /api/admin/audit` reading from `audit_projection` (not `audit_log` directly)
5. Cross-tenant bypass handling (ADR-025 D5 `CrossTenantReportAccessed` events visible to GlobalAdmin only; LocalAdmin queries scope by `audit_projection.target_org_id IN (subtree_org_ids)`)
6. Migration: backfill `audit_projection` from existing `events` table for pre-existing audit-relevant event types
7. Phase E continuous-validation tests: every audit-relevant event registered in EventSerializer has a projection mapping; projection backfill idempotent

**Path A / B / D rejected**: A insufficient (operator/system actions invisible — explicit launch concern); B too broad (middleware retrofit + per-endpoint target_org_id mapping for every state-changing endpoint); D unnecessary complexity (union semantics). C is the user's choice + aligns with S27 ProjectionBackfillService precedent.

## Interim-Expert Posture (carry-over from S37 + S38)

Same as S38. ADR-026 is system-design + security-correctness; not Phase B dependent. Architecture stands regardless.

## Phase Decomposition

Orchestrator-direct sequential per S38 / S32 / S28 design-only precedent.

| Phase | Tasks | Dispatch |
|-------|-------|----------|
| 0 | TASK-38B-00 | Sprint open (this file + SPRINT-38b log + INDEX) |
| 1 | TASK-38B-01 | ADR-026 authorship (Orchestrator-direct per WORKFLOW.md KB write rule) |
| 2 | TASK-38B-02 | Step 7a-equivalent dual-lens review (Codex external + Reviewer Agent; cycle-cap 2; **MANDATORY per `sprint-close-guard.ps1` hook**) |
| 3 | TASK-38B-03 | Sprint close |

## Step 0a — Entropy Scan

| Check | Result |
|-------|--------|
| KB path validation | CLEAN — ADR-026 placeholder already exists at `docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md` (filed `dadc1b4`); this sprint replaces PLANNED content with ACCEPTED design |
| ADR numbering | CLEAN — 026 reserved for this purpose |
| Mechanical gate | ACTIVE per `297fdee`; S38b close commit gated by hook |
| Path (C) viability | Confirmed — ADR-018 D13 sync-in-tx projection pattern + S27 ProjectionBackfillService precedent + ADR-001 immutability all support path C without conflict |

## Step 0b — Plan Review

**SKIP** — design-only sprint per S28 / S32 / S38 precedent. Step 7a-equivalent at close is the formal review gate.

## Architectural Constraints

_Checked at close._

- [ ] **P1 — Architectural integrity** → New KB entry; cross-references to ADR-001 + ADR-018 D13 + ADR-024 + ADR-025 + ADR-013 preserved
- [ ] **P3 — Event sourcing / auditability** → Event log stays immutable per ADR-001; projection is the read surface (matches S27 pattern for time_entries / absences projections)
- [ ] **P7 — Security / access control** → Tenant scope binding preserved via `audit_projection.target_org_id IN (subtree_org_ids)`; cross-tenant bypass per ADR-025 D5 documented as single GlobalAdmin-only exception

## Task Log

4 declared tasks (TASK-38B-00..03).

### Phase 0 — Sprint Open

#### TASK-38B-00 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-38B-00 |
| **Status** | in-progress |
| **Components** | `.claude/plans/PLAN-s38b.md`, `docs/sprints/SPRINT-38b.md`, `docs/sprints/INDEX.md` |

### Phase 1 — ADR-026 Authorship (TASK-38B-01)

Rewrite `docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md` from PLANNED placeholder to full ACCEPTED design with path (C) chosen. Settles D1-D7 (TBD count) covering schema + projection mechanism + event inventory + query surface + cross-tenant bypass + migration + Phase E tests.

### Phase 2 — Step 7a Dual-Lens (TASK-38B-02)

Codex external + Reviewer Agent in parallel against S38b diff. Cycle-cap 2 per lens. Artifacts at `.claude/reviews/SPRINT-38b-step7a-{codex,reviewer}.md` with verdict lines. Per the post-S38-D7-defer experience: review focus includes whether the path (C) design closes the cycle-1/cycle-2/cycle-3 D7 concerns substantively (not just textually).

### Phase 3 — Sprint Close (TASK-38B-03)

Close sections + INDEX + MEMORY. Sprint-close commit through hook.

## Forward Pointers

- **S39 schema migration** — adds `audit_projection` table to the existing 6 ledger entries from S38; ADR-026 backfill seeder runs as part of greenfield-baked migration
- **S40 cutover** — implements ADR-026 endpoint + UI + per-event projection mappings; covers ADR-024's 7 new events + ADR-025's 4 new events with projection declarations
- **S41 D-tests** — cross-tenant audit leakage + projection backfill idempotency + event-coverage assertions (every EventSerializer-registered audit-relevant event has a projection mapping)
