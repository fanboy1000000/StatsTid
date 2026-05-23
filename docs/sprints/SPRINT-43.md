# Sprint 43 — ADR-026 Sub-Sprint 1 (Audit Visibility: Schema + Repo + Interface + Backfill)

| Field | Value |
|-------|-------|
| **Sprint** | 43 |
| **Status** | provisional (sprint-open) |
| **Start Date** | 2026-05-23 |
| **End Date** | _pending_ |
| **Sprint-start commit base** | `b2519ea` (S42a close) |
| **Sprint type** | Implementation (plumbing only — schema + repo + interface + registry + backfill + catalog + 2 cutover-independent Phase E tests + 1 legacy-migration safety test) |
| **Phase** | 4e (Phase E audit visibility) |
| **Plan** | `.claude/plans/PLAN-s43.md` |
| **Refinement** | `.claude/refinements/REFINEMENT-s43-adr026-sub-sprint-1.md` (gitignored; Step 4 cycle 1 + Step 0b cycles 1+2 absorbed) |

## Sprint Goal

Lay the audit-visibility plumbing per ADR-026 D1/D2/D3/D7: `audit_projection` schema + `AuditProjectionRepository` + `IAuditProjectionMapper<T>` interface + `IAuditProjectionMapperRegistry` + `AuditProjectionBackfillService` + initial `audit-projection-catalog.md` + 3 cutover-independent Phase E tests. Mirror of S40 ADR-024 Sub-Sprint 1 shape. Sub-Sprint 2 (S44) wires ~53 endpoint mapper sites + GET `/api/admin/audit` + `AuditLogView.tsx` + resolves 2 known seams (Payroll dispatch + L194 reconciliation). Sub-Sprint 3 (S45) lands D-tests #1/#3/#4.

## Pre-sprint context

8 tasks (TASK-4300..4307). 9th sprint slot in the Phase 4e architectural surge (S38→S38b→S39→S40→S41→S41a→S42→S42a→S43). ADR-024 D1+D2 cutover SUSPENDED per S42a discipline-rollback; ADR-026 work proceeds independently because path C event-projection was designed at S38b specifically to avoid the cross-process issues that derailed ADR-024.

## Step 4 + Step 0b cycle-trail summary

**Refinement Step 4 cycle 1** (Codex external + Reviewer Agent internal): 2 convergent BLOCKERs caught by the new `feedback_cross_process_caller_census_required.md` discipline:
1. `RetroactiveCorrectionRequested` cross-process emitter from Payroll.Integrations via `IEventStore.AppendAsync` (not ADR-026 D2's required `(conn, tx)` + `IOutboxEnqueue.EnqueueAndReturnIdAsync` shape) — absorbed via `mapper_kind: TBD-payroll-dispatch-seam` catalog marker; Sub-Sprint 2 resolves between (i) Payroll site rewrite OR (ii) narrow ADR-026 errata.
2. Backfill startup gate misread — absorbed via unconditional `RunAsync()` per S27 precedent.

**Plan Step 0b cycle 1**: 4 BLOCKERs (3 Codex divergent + 1 Reviewer divergent — no overlap):
- Codex B1: "backfill-only inclusion" option violates ADR-026 D2 sync-in-tx → struck.
- Codex B2: TASK-4306 Test #2 idempotency vacuous on pre-mapper greenfield → strengthened (seeded event + test mapper + production AuditProjectionBackfillService).
- Codex B3: legacy migration safety under-validated → NEW Test #6 added.
- Reviewer B1: ADR-026 L182 vs L194 internal contradiction for 4 `EmployeeProfile*` events → 4 catalog rows get `mapper_kind: TBD-l194-reconciliation` (parallel to TBD-payroll-dispatch-seam precedent).

**Plan Step 0b cycle 2**: 0 BLOCKERs + 7 WARNINGs (all refinement-body staleness; PLAN internally consistent). Reviewer Agent verdict verbatim: *"No new BLOCKER introduced; no smoke-alarm."* All 7 WARNINGs absorbed mechanically. **Step 0b complete — sprint open authorized.**

## Cycle-trail discipline note

The new caller-census discipline (`feedback_cross_process_caller_census_required.md`, created after S42a discipline-rollback) **worked as designed at refinement Step 4 cycle 1** — caught a cross-process seam BEFORE implementation thrash. Step 0b found 4 additional issues across both lenses (all mechanical absorptions). No same-area cycle-trail thrash; cycle 2 converged to clean.

## Tasks

| Task | Status | Owner | Notes |
|------|--------|-------|-------|
| TASK-4300 | in_progress | Orchestrator | Sprint open (this commit) |
| TASK-4301 | pending | Orchestrator | `audit_projection` schema + 5 idx_* indexes + CHECK constraint |
| TASK-4302 | pending | Builder | `AuditProjectionRepository` (InsertAsync + CountAsync + CountByEventIdAsync); Backend.Api DI only (Payroll deferred to S44) |
| TASK-4303 | pending | Orchestrator | `IAuditProjectionMapper<T>` + records + `IAuditProjectionMapperRegistry` (interface in SharedKernel; impl in Infrastructure) |
| TASK-4304 | pending | Builder | `AuditProjectionBackfillService` (S27 SSOT pattern); unconditional Backend startup hook |
| TASK-4305 | pending | Orchestrator | `docs/operations/audit-projection-catalog.md` initial draft with locked 7-column markdown table |
| TASK-4306 | pending | Builder | Phase E tests #2 + #5 + #6 (backfill idempotency strengthened + schema CHECK + legacy migration safety) |
| TASK-4307 | pending | Orchestrator | Sprint close (Step 7a dual-lens + INDEX + ROADMAP + MEMORY + QUALITY re-grade) |

## Forward Pointers

- **S44 = ADR-026 Sub-Sprint 2 (Cutover)**: ~53 per-event mapper implementations + `OrgScopeValidator.GetAccessibleOrgsAsync` + `GET /api/admin/audit` + `AuditLogView.tsx`. Resolves both Sub-Sprint 1 seams (`TBD-payroll-dispatch-seam` + `TBD-l194-reconciliation`).
- **S45 = ADR-026 Sub-Sprint 3 (D-tests)**: 3 cutover-dependent Phase E tests (#1 event-coverage + #3 sync-in-tx + #4 per-class visibility).
- **Customer-go-live** unblocked architecturally after S44 close per ROADMAP L391/L394.
