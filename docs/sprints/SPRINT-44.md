# Sprint 44 — ADR-026 Sub-Sprint 2 (Audit Visibility cutover — NARROW SCOPE)

| Field | Value |
|-------|-------|
| **Sprint** | 44 |
| **Status** | complete |
| **Start Date** | 2026-05-23 |
| **End Date** | 2026-05-24 |
| **Sprint-start commit base** | `f62b8cb` (S43 close) |
| **Sprint-close commit** | _this commit_ |
| **Build Verified** | 0 errors / 0 net-new warnings |
| **Test Verified** | 18 net new Docker-gated D-tests (12 cutover + 6 query); 893 total |
| **Orchestrator Approved** | yes — 2026-05-24 |
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

## Tasks

| Task | Status | Commit | Notes |
|------|--------|--------|-------|
| TASK-4400 | complete | `f38bad6` | Sprint open |
| TASK-4401 | complete | `546c57a` | `docs/operations/audit-projection-caller-census.md` |
| TASK-4402 | complete | `53df715` | ADR-026 D3 clarification block above L194 (OQ3) |
| TASK-4403 | complete | `a8eb046` | Catalog TBD-* marker updates (OQ2 + OQ4 + PayrollExportGenerated) |
| TASK-4404 | complete | `2036103` | Catalog header TBD-suffix marker taxonomy + ix/idx drift note |
| TASK-4405 | complete | `23fd7d6` | `OrgScopeValidator.GetAccessibleOrgsAsync(ActorContext, ct)` |
| TASK-4406 | complete | `3ca0756` | `AuditProjectionRepository.QueryByOrgScopeAsync` + 6 D-tests |
| TASK-4407..4412 | complete | `2de58c6` | 6 IAuditProjectionMapper impls + shared AuditMapperJsonOptions |
| TASK-4413 | complete | `630800d` | 6 AdminEndpoints.cs cutover sites (EnqueueAsync → EnqueueAndReturnIdAsync + audit insert) |
| TASK-4414 | complete | `bba76aa` | 12 Phase E D-tests (6 happy-path + 6 forced-rollback cutover atomicity) |
| TASK-4415 | complete | _this commit_ | Sprint close (Step 7a + INDEX + SPRINT-44 + stale-pointer reconciliation) |

## Step 7a Outcome

Both lenses **APPROVED-WITH-WARNINGS**, 0 BLOCKERs. Cycle-cap = 1 per lens (no cycle 2 needed — all findings are WARNINGs/NOTEs, no architectural surprises).

**Convergent findings**:
- **W1 (Codex + Reviewer)**: `RoleAssignmentRevoked` endpoint at `AdminEndpoints.cs:1617` calls `userRepo.GetByIdAsync(assignmentUserId, ct)` which opens its own connection outside the active `(conn, tx)` — architecturally inconsistent with the S24 pattern where all reads inside the tx boundary use `(conn, tx)` overloads. Not a correctness bug under normal operation (rollback path still works correctly), but violates the established pattern. **Deferred to S44b** — requires adding a `(conn, tx)` overload to `UserRepository.GetByIdAsync` which is an Infrastructure domain change.

**Divergent findings**:
- **Codex W2**: `filter.TargetOrgId` in `QueryByOrgScopeAsync` is not validated against `accessibleOrgIds` for GLOBAL_TENANT_VISIBLE rows — a LocalAdmin could see GTV rows with a non-null `target_org_id` outside their scope if such rows existed (no mapper currently writes GTV with non-null target_org_id). Low risk; consider adding a CHECK constraint `(visibility_scope != 'GLOBAL_TENANT_VISIBLE' OR target_org_id IS NULL)` at S44f.
- **Reviewer W2**: `AuditQueryFilter.ActorId` clause `(actor_id = @actorId OR actor_primary_org_id = @actorId)` compares a user-ID against an org-ID column — semantic type confusion. Not a security issue (read filter for display), but could produce surprising results if IDs overlap. Deferred to S44f.
- **Codex N1**: `new DateTimeOffset(@event.OccurredAt)` at all 6 endpoint sites doesn't defensively `SpecifyKind(Utc)` like the test helper does. Low risk — `DomainEventBase.OccurredAt` defaults to `DateTime.UtcNow`.
- **Codex N2**: `ActorContext.OrgId` → `ActorPrimaryOrgId` naming asymmetry. Cosmetic.
- **Reviewer N1-N6**: SQL parenthesization verified correct; `GetDescendantsAsync` scope-org-inclusive confirmed by D-test; single-enqueue rollback tests are trivially-true by design (audit insert never executes); cross-domain boundary respected; SQL fully parameterized.

## Test Counts

| Suite | S43 | S44 | Delta |
|-------|-----|-----|-------|
| Unit | 526 | 526 | 0 |
| Plain regression | 40 | 40 | 0 |
| Docker-gated | 219 | 237 | +18 |
| Frontend vitest | 90 | 90 | 0 |
| **Total** | **875** | **893** | **+18** |

S44 new D-tests:
- `AuditProjectionQueryTests.cs`: 6 D-tests (org-scope filter across 3 visibility tiers × positive/negative/GlobalAdmin)
- `AuditProjectionCutoverTests.cs`: 12 D-tests (6 happy-path endpoint emit chain + 4 single-enqueue forced-rollback + 2 multi-enqueue post-audit-insert forced-rollback)

## Forward Pointers

- **S44b** = mid-size mapper families (Config + Period + Overtime + UserAgreementCode — partition decided at S44b refinement) + convergent W1 fix (UserRepository.GetByIdAsync `(conn, tx)` overload)
- **S44c** = remaining mapper families including EmployeeProfile* (from OQ3 resolution; HR-sensitive payload-redaction check at refinement) + LocalAgreementProfileChanged
- **S44f** = GET /api/admin/audit endpoint + AuditLogView.tsx + Phase E Test #1 (catalog ↔ DI ↔ EventSerializer parity) + Test #3 (sync-in-tx assertion) + Test #4 (per-class visibility enforcement) + PayrollExportGenerated emit-or-delete decision + Codex W2 (GTV CHECK constraint) + Reviewer W2 (ActorId/OrgId type confusion)
- **S44-cross-process** = dedicated 1-event sprint solving RetroactiveCorrectionRequested + Payroll DbConnectionFactory introduction holistically
