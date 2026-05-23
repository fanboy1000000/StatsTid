# Sprint 43 — ADR-026 Sub-Sprint 1 (Audit Visibility: Schema + Repo + Interface + Backfill)

| Field | Value |
|-------|-------|
| **Sprint** | 43 |
| **Status** | complete |
| **Start Date** | 2026-05-23 |
| **End Date** | 2026-05-23 |
| **Sprint-start commit base** | `b2519ea` (S42a close) |
| **Sprint-close commit** | _this commit_ |
| **Build Verified** | 0 errors / 0 net-new warnings |
| **Test Verified** | 7 S43 Phase E tests pass first run (6 net new + 1 sibling); 875 total |
| **Orchestrator Approved** | yes — 2026-05-23 |
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

| Task | Status | Commit | Notes |
|------|--------|--------|-------|
| TASK-4300 | complete | `bea7d75` | Sprint open (PLAN + SPRINT doc + INDEX provisional) |
| TASK-4301 | complete | `8fd9295` | `audit_projection` schema + 5 idx_* indexes + 2 CHECK constraints |
| TASK-4302 | complete | `911679f` | `AuditProjectionRepository`; Backend.Api DI only |
| TASK-4303 | complete | `5db2118` | `IAuditProjectionMapper<T>` + records + Registry interface in SharedKernel; impl in Infrastructure |
| TASK-4304 | complete | `4f92ab9` | `AuditProjectionBackfillService` mirroring S27 SSOT + unconditional Backend startup hook + tools/ProjectionBackfill --target flag |
| TASK-4305 | complete | `795c0ca` | `docs/operations/audit-projection-catalog.md` with 53 rows + 2 TBD-with-suffix markers per Step 0b cycle 1 absorptions |
| TASK-4306 | complete | `dca434b` | Phase E tests #2 + #5 + #6 — 6 net new Docker-gated tests passing first run |
| TASK-4307 | complete | `e482b24` (cycle 1 absorption) + this commit | Step 7a dual-lens (0 BLOCKERs, 4 WARNINGs absorbed); INDEX + ROADMAP + MEMORY + QUALITY updates |

## Step 7a Outcome

Both lenses **APPROVED-WITH-WARNINGS**, 0 BLOCKERs, divergent first-cycle findings (no overlap between lenses — normal divergence pattern). All 4 WARNINGs absorbed inline in `e482b24`:

- **Codex W1** (backfill full-scan) → added `RegisteredEventTypeNames` filter + `RegisteredAuditEventType` marker; empty filter = fast-path no-op
- **Codex W2** (ADR-026 D2 signature drift) → explicit xmldoc paragraph in `IAuditProjectionMapper.cs` documenting Map vs MapToRow + DateTimeOffset vs DateTime + string vs JsonDocument
- **Reviewer W1** (4 ADR-025 events unregistered) → catalog "Known inventory gaps" extended with explicit S44 follow-up
- **Reviewer W2** (counter naming) → `PreS22Skipped` → `NullOutboxSkipped` (also catches post-S22 anomalies)

5 NOTEs acknowledged in artifacts; 1 absorbed (project path correction); 4 deferred (cosmetic / parallels S27 / Phase 4e candidates).

Step 7a artifacts: `.claude/reviews/SPRINT-43-step7a-codex.md` + `.claude/reviews/SPRINT-43-step7a-reviewer.md` with verdict + reviewed-against-commit per sprint-close-guard hook contract.

## Forward Pointers

- **S44 = ADR-026 Sub-Sprint 2 (Cutover)**: ~53 per-event mapper implementations + `OrgScopeValidator.GetAccessibleOrgsAsync` + `GET /api/admin/audit` + `AuditLogView.tsx`. Resolves both Sub-Sprint 1 seams (`TBD-payroll-dispatch-seam` + `TBD-l194-reconciliation`).
- **S45 = ADR-026 Sub-Sprint 3 (D-tests)**: 3 cutover-dependent Phase E tests (#1 event-coverage + #3 sync-in-tx + #4 per-class visibility).
- **Customer-go-live** unblocked architecturally after S44 close per ROADMAP L391/L394.
