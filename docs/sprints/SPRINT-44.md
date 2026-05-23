# Sprint 44 — ADR-026 Sub-Sprint 2 (Audit Visibility cutover — NARROW SCOPE)

| Field | Value |
|-------|-------|
| **Sprint** | 44 |
| **Status** | provisional (sprint-open) |
| **Start Date** | 2026-05-23 |
| **End Date** | _pending_ |
| **Sprint-start commit base** | `f62b8cb` (S43 close) |
| **Sprint type** | Implementation (cutover-class, first of multi-sub-sprint cutover series) |
| **Phase** | 4e (Phase E audit visibility — ADR-026 Sub-Sprint 2, narrowed) |
| **Plan** | `.claude/plans/PLAN-s44.md` |
| **Refinement** | `.claude/refinements/REFINEMENT-s44-adr026-sub-sprint-2.md` (gitignored; Step 4 cycle 1 + Step 0b cycle 1 absorbed) |

## Sprint Goal

Lay the cutover plumbing per ADR-026 D2 + ship 1 exemplar mapper family (Org/User/RoleAssignment = 6 events) end-to-end. Mirror of S43 Sub-Sprint 1 shape (plumbing first, exemplar second). Remaining ~47 mappers + GET endpoint + frontend split across S44b/c/f per cycle-trail discipline. NOT the full Sub-Sprint 2 as originally projected at S38b — narrowing rationale per S40→S41 + S33 single-pattern-application precedents + ADR-024 Sub-Sprint 2 thrash precedent.

## Adjudications resolved at refinement (dual-lens consensus)

- **OQ2** (RetroactiveCorrectionRequested cross-process emit): defer to dedicated cross-process sprint via `TBD-cross-process-deferred` catalog marker. Preserves Payroll's HTTP-orchestrating-only Phase 4 boundary.
- **OQ3** (ADR-026 L182 vs L194 EmployeeProfile* contradiction): L182 wins; ADR-026 D3 clarification block (post-ACCEPTED disclaimer per `a0e30ed` pattern); 4 EmployeeProfile* mappers land in S44b/c (HR-sensitive payload-redaction check at that refinement).
- **OQ4** (4 ADR-025 events unregistered): defer via `TBD-adr025-implementation-pending` marker; ADR-025 features land in dedicated post-launch sprints.
- **PayrollExportGenerated** (caller-census surfaced): `TBD-defined-but-unemitted` marker (defined in SharedKernel + EventSerializer but zero production emit sites; vestigial S22 leftover).

## Pre-sprint context

15 declared tasks (TASK-4400..4415). 10th sprint slot in Phase 4e architectural surge (S38→S38b→S39→S40→S41→S41a→S42→S42a→S43→S44). ADR-024 D1+D2 cutover SUSPENDED per S42a discipline-rollback; ADR-026 path C proceeds independently because designed at S38b specifically to avoid cross-process issues.

## Step 4 + Step 0b cycle-trail summary

**Refinement Step 4 cycle 1**: 1 convergent BLOCKER (EnqueueAsync → EnqueueAndReturnIdAsync conversion needed at 6 AdminEndpoints sites; both lenses agreed) + 8 WARNINGs (2 convergent on ActorContext.OrgId + signature drift; 6 divergent — Codex mapper-DI/AC-weakness/same-endpoint-coupling, Reviewer GetAccessibleOrgsAsync-signature/errata-terminology) + 8 NOTEs. All absorbed mechanically; no cycle 2.

**Plan Step 0b cycle 1**: 1 BLOCKER (Codex — forced-rollback test design unimplementable as written) + 3 Codex WARNINGs (ActorContext binding, JsonSerializerOptions, QueryByOrgScopeAsync deferral hint) + 0 Reviewer BLOCKERs + 7 Reviewer NOTEs (2 convergent with Codex). All absorbed; no cycle 2.

## Cycle-trail discipline note

New `feedback_no_launch_gate_scope_logic.md` (this session) prohibits using launch-gate logic to shape scope. Narrow-S44 framing rests on cycle-trail discipline + sprint-sizing precedent alone. New `feedback_cross_process_caller_census_required.md` (from S42a) applied at refinement Phase A — caller-census surfaced single cross-process audit emitter (RetroactiveCorrectionRequested) + 1 vestigial event class (PayrollExportGenerated) BEFORE plan-writing.

## Tasks

| Task | Status | Owner | Notes |
|------|--------|-------|-------|
| TASK-4400 | in_progress | Orchestrator | Sprint open (this commit) |
| TASK-4401 | pending | Orchestrator | `docs/operations/audit-projection-caller-census.md` |
| TASK-4402 | pending | Orchestrator | ADR-026 D3 clarification block above L194 (OQ3) |
| TASK-4403 | pending | Orchestrator | Catalog TBD-* marker updates (OQ2 + OQ4 + PayrollExportGenerated) |
| TASK-4404 | pending | Orchestrator | Catalog header marker semantics doc + ix/idx naming note |
| TASK-4405 | pending | Builder | `OrgScopeValidator.GetAccessibleOrgsAsync(ActorContext, ct)` |
| TASK-4406 | pending | Builder | `AuditProjectionRepository.QueryByOrgScopeAsync` + Docker-gated D-test |
| TASK-4407..4412 | pending | Builder | 6 mappers + shared `AuditMapperJsonOptions` (7 files) + DI registration pairs |
| TASK-4413 | pending | Builder | 6 AdminEndpoints.cs cutover sites (L154/237/587/1033/1429/1542) + Minimal API binding pattern |
| TASK-4414 | pending | Builder | 13 Phase E D-tests (6 happy + 6 forced-rollback two-shape + 1 QueryByOrgScopeAsync) |
| TASK-4415 | pending | Orchestrator | Sprint close (Step 7a + INDEX + SPRINT-44 + SPRINT-43 + init.sql forward-pointer reconciliation + ROADMAP + MEMORY + QUALITY) |

## Forward Pointers

- **S44b** = mid-size mapper families (Config + Period + Overtime + UserAgreementCode — partition decided at S44b refinement)
- **S44c** = remaining mapper families including EmployeeProfile* (from OQ3 resolution; HR-sensitive payload redaction check at refinement) + LocalAgreementProfileChanged
- **S44f** = GET /api/admin/audit endpoint + AuditLogView.tsx + Phase E Test #1 (catalog ↔ DI ↔ EventSerializer parity) + Test #3 (sync-in-tx assertion) + Test #4 (per-class visibility enforcement) + PayrollExportGenerated emit-or-delete decision
- **S44-cross-process** = dedicated 1-event sprint solving RetroactiveCorrectionRequested + Payroll DbConnectionFactory introduction holistically
