# PLAN — Sprint 44: ADR-026 Sub-Sprint 2 (Audit Visibility cutover — NARROW SCOPE)

| Field | Value |
|-------|-------|
| **Sprint** | 44 |
| **Phase** | 4e (Phase E audit visibility — ADR-026 Sub-Sprint 2, narrowed) |
| **Sprint type** | Implementation (cutover-class, first of multi-sub-sprint cutover series) |
| **Base commit** | `f62b8cb` (S43 close) |
| **Refinement** | `.claude/refinements/REFINEMENT-s44-adr026-sub-sprint-2.md` (READY post Step 4 cycle 1 absorption — 1 convergent BLOCKER + 8 WARNINGs absorbed inline) |
| **Sprint open date** | 2026-05-23 |
| **Task count** | 15 (TASK-4400..4415) |

## Sprint Goal

Lay the cutover plumbing per ADR-026 D2 + ship 1 exemplar mapper family (Org/User/RoleAssignment = 6 events) end-to-end. Remaining ~47 mappers + GET endpoint + frontend split across S44b/c/f per cycle-trail discipline. NOT the full Sub-Sprint 2 as originally projected at S38b — narrowing rationale per S40→S41 + S33 single-pattern-application precedents + ADR-024 Sub-Sprint 2 thrash precedent.

## Adjudications resolved at refinement (dual-lens consensus)

- **OQ2** (RetroactiveCorrectionRequested): (iii) defer to dedicated cross-process sprint via `TBD-cross-process-deferred` marker
- **OQ3** (L182 vs L194): (a) L182 wins; ADR-026 D3 clarification block; EmployeeProfile* mappers land in S44b/c
- **OQ4** (4 ADR-025 events): (a) defer via `TBD-adr025-implementation-pending` marker
- **PayrollExportGenerated** (caller-census surfaced): `TBD-defined-but-unemitted` marker

## Step 4 Absorption Summary

**1 convergent BLOCKER** (Codex + Reviewer): 6 AdminEndpoints sites use `EnqueueAsync` not `EnqueueAndReturnIdAsync`. **Absorbed**: TASK-4413 explicitly includes the 6 conversions.

**8 WARNINGs** absorbed inline (2 convergent + 6 divergent):
- ActorContext property name (`.OrgId` not `.PrimaryOrgId`)
- Mapper DI via Minimal API parameter binding (not constructor injection)
- AC for "within same tx" weak → 6 forced-rollback D-tests added (S27 precedent)
- Same-endpoint coupling (S44b/c will reopen same files) → Risk #5
- GetAccessibleOrgsAsync signature → `(ActorContext, ct)` not `(actorId, ct)`
- "Errata" terminology → "post-ACCEPTED clarification block" (pre-rule projection disclaimer precedent)
- TBD-adr025-implementation-pending 4× rot risk → Risk #8
- Mapper signature uses SHIPPED contract → Phase 3 explicitly notes

## Step 0a — Entropy Scan

| Check | Result |
|-------|--------|
| Schema migration ledger | `s44-d1-*` not needed (no schema changes in S44; audit_projection table from S43) |
| EventSerializer count | 65 (post-S40); unchanged by S44 (path C uses existing types) |
| Cross-process caller census | DONE per refinement Phase A — 1 cross-process emitter at Payroll/RetroactiveCorrectionService.cs:222 (deferred); no Orchestrator emitters; in-process emit shape verified for 6 S44 cutover sites |
| AdminEndpoints emit-site shape verification | 6 sites at L154/237/587/1033/1429/1542 use `EnqueueAsync` → conversion to `EnqueueAndReturnIdAsync` required (BLOCKER absorption) |
| Pattern references verified | S27 ProjectionBackfillService SSOT + TimeProjectionAtomicTests forced-rollback precedent for D-tests; S43 AuditProjectionRepository.InsertAsync contract |

## Step 0b — Plan Review

**MANDATORY** per WORKFLOW.md trigger criteria — sprint touches:
- **P1** (Architectural integrity — first cutover invocation of ADR-026 D2 dispatch contract)
- **P3** (Event sourcing — atomic-outbox tx-span widening to include projection write)
- **P7** (Security — visibility_scope + scope-by-target enforcement at endpoint layer)
- **Cutover-class work** — higher cycle-1 BLOCKER risk per ADR-024 Sub-Sprint 2 precedent

Dispatch dual-lens (Codex external + Reviewer Agent internal) on this PLAN file before Phase 1 tasks. Cycle-cap = 2 per lens.

### Cycle 1 — 2026-05-23 — ABSORBED

**Codex (1 BLOCKER + 3 WARNINGs + 3 NOTEs)**:
- **BLOCKER**: Forced-rollback test design "throw AFTER audit insert" not implementable (AuditProjectionRepository sealed; non-virtual InsertAsync; S27 actually throws BEFORE insert). **ABSORBED**: TASK-4414 rewritten with two-shape design — single-enqueue endpoints throw on EnqueueAndReturnIdAsync (S27 pattern); multi-enqueue endpoints (POST/PUT users) throw on SECOND enqueue (after audit insert) to prove post-insert rollback.
- **W1** ActorContext binding in Minimal API lambda — not DI-registered; existing pattern is `HttpContext context` + `context.GetActorContext()`. **ABSORBED**: TASK-4413 explicitly notes the binding pattern.
- **W2** JsonSerializerOptions for mapper Details serialization underspecified. **ABSORBED**: TASK-4407 lands shared `AuditMapperJsonOptions.cs` static class first; TASK-4408..4412 reference it.
- **W3** QueryByOrgScopeAsync read-path plumbing might defer to S44f. **ACKNOWLEDGED, retained in S44**: Phase E groundwork; D-test exercises the projection-read contract end-to-end alongside the exemplar mappers. If S44 scope pressure surfaces mid-sprint, TASK-4406 is the natural defer candidate.
- 3 NOTEs all positive confirmations of absorption integrity + Minimal API binding architectural fit + DI ordering safety.

**Reviewer Agent (0 BLOCKERs + 7 NOTEs)**:
- **NOTE-1** ActorContext binding (convergent with Codex W1). **ABSORBED** above.
- **NOTE-2** DateTimeOffset conversion safety verified for `DateTime.UtcNow` (Kind=Utc → TimeSpan.Zero). **ABSORBED**: TASK-4413 components note explicitly.
- **NOTE-3** JsonSerializerOptions choice (convergent with Codex W2). **ABSORBED** above.
- **NOTE-4** TASK-4406 D-test "3 rows" vs "9 rows" wording ambiguity. **ABSORBED**: rewrote as "seed 3 orgs + 9 audit_projection rows (3 per org)".
- **NOTE-5** Risk numbering glitch in refinement (cosmetic; doesn't carry into plan).
- **NOTE-6** Verify EventSerializer count = 65 at close. **ABSORBED**: TASK-4415 Components explicit.
- **NOTE-7** D-test fixture must TRUNCATE audit_projection between tests (S43 backfill startup would otherwise populate). **ABSORBED**: TASK-4414 Fixture row added.

**Verdict**: 1 BLOCKER + 2 convergent WARNINGs + 5 mechanical NOTEs all absorbed mechanically. No new architectural surprises. Cycle 2 not required per cycle-cap discipline.

### Cycle 2 — Not dispatched

Cycle 1 absorption was missed-facts (S43 plumbing realities + Minimal API binding semantics + S27 test-precedent shape) — single-cycle finite absorption per `feedback_thrash_defer_real_world.md`. Cycle 2 dispatch reserved for cases where cycle 1 absorption surfaces NEW architectural questions; that did not occur here.

## Architectural Constraints

_Checked at close._

- [ ] **P1** Architectural integrity — endpoint-direct mapper invocation per ADR-026 D2; mapper invocation rides existing atomic-outbox tx per ADR-018 D3; no event-handler bus
- [ ] **P3** Event sourcing — projection write atomic with event log per ADR-018 D13; mapper is pure synchronous; backfill SSOT preserved
- [ ] **P5** Integration isolation — Payroll cross-process emit explicitly deferred via TBD marker; no cross-process boundary widening in S44
- [ ] **P7** Security — `visibility_scope` CHECK + `chk_target_org_required_when_tenant` already enforced at schema layer (S43); GET endpoint scope-by-target lands in S44f
- [ ] **P8** CI/CD — forced-rollback D-tests gate atomic-tx-rollback semantic at every endpoint

## Task Log

### Phase 0 — Sprint Open

#### TASK-4400 — Sprint-open plumbing
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s44.md` + `docs/sprints/SPRINT-44.md` + `docs/sprints/INDEX.md` provisional |

### Phase 1 — Caller-census + 3 Adjudication ADR clarifications (TASK-4401..4404)

#### TASK-4401 — Caller-census artifact
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/operations/audit-projection-caller-census.md` — table of every catalog event's emit site(s) with atomic-outbox-shape annotation |
| **Validation** | Markdown parses; covers all 53 catalog events; cross-process sites flagged |

#### TASK-4402 — ADR-026 D3 clarification block (OQ3)
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md` — inline clarification block above L194 (`**ADR-026 clarification 2026-05-23 (S44):**` prefix per pre-rule projection disclaimer pattern); catalog drops 4 EmployeeProfile* TBD-l194-reconciliation markers |
| **Validation** | ADR text remains in ACCEPTED status; clarification block follows the a0e30ed disclaimer convention |

#### TASK-4403 — Catalog TBD-with-suffix marker updates (OQ2 + OQ4 + PayrollExportGenerated)
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/operations/audit-projection-catalog.md` row updates: RetroactiveCorrectionRequested → TBD-cross-process-deferred; 4 ADR-025 rows → TBD-adr025-implementation-pending; PayrollExportGenerated → TBD-defined-but-unemitted |
| **Validation** | All catalog row updates present; markdown table parses; row count still 53 |

#### TASK-4404 — Catalog header marker semantics doc + ix/idx naming note
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | Catalog header documents the 4 TBD-with-suffix marker semantics + Phase E Test #1 tolerance contract + pre-existing `ix_*` (ADR-026 D5 SQL text) vs `idx_*` (init.sql) naming drift note (Reviewer N4) |

### Phase 2 — Plumbing (TASK-4405..4406)

#### TASK-4405 — OrgScopeValidator.GetAccessibleOrgsAsync
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Builder Agent (mechanical extension of S31 OrgScopeValidator pattern) |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/Security/OrgScopeValidator.cs` — new `GetAccessibleOrgsAsync(ActorContext actor, CancellationToken ct)` method returning `IReadOnlyList<string>?` (null sentinel for GlobalAdmin). Per-role behavior: GlobalAdmin → null; LocalAdmin → materialized-path descendants of `actor.Scopes` orgs; other → empty list |
| **Validation** | `dotnet build` clean; unit tests cover all 3 role cases |

#### TASK-4406 — AuditProjectionRepository.QueryByOrgScopeAsync + Docker-gated D-test
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Builder Agent |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/AuditProjectionRepository.cs` — new `QueryByOrgScopeAsync(IReadOnlyList<string>? accessibleOrgIds, AuditQueryFilter filter, int page, int pageSize, CancellationToken ct)` returning `(IReadOnlyList<AuditProjectionRow> rows, int totalCount)`; AuditQueryFilter record for filter dimensions (event_types, target_org_id, actor_id, occurred_at_from/to, visibility_scopes); WHERE clause combines (a) `target_org_id = ANY(@orgIds)` OR (b) `visibility_scope = 'GLOBAL_TENANT_VISIBLE'` OR (c) GlobalAdmin GLOBAL_ADMIN_ONLY when accessibleOrgIds=null |
| **Validation** | `dotnet build` clean; 1 Docker-gated D-test exercising all 3 visibility tiers across 3 orgs × 3 rows |

### Phase 3 — 6 Exemplar Mappers (TASK-4407..4412)

#### TASK-4407..4412 — 6 IAuditProjectionMapper implementations
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Builder Agent (mechanical pattern application; one mapper per task) |
| **Components** | New directory `src/Backend/StatsTid.Backend.Api/AuditMappers/` with **7 files**: 6 mappers (`OrganizationCreatedAuditMapper.cs`, `OrganizationUpdatedAuditMapper.cs`, `UserCreatedAuditMapper.cs`, `UserUpdatedAuditMapper.cs`, `RoleAssignmentGrantedAuditMapper.cs`, `RoleAssignmentRevokedAuditMapper.cs`) + **1 shared `AuditMapperJsonOptions.cs`** (static class exposing canonical `JsonSerializerOptions` instance with `JsonSerializerDefaults.Web` camelCase + IgnoreNullValues) per Step 0b cycle 1 Codex W2 + Reviewer NOTE-3 absorption — TASK-4407 lands the shared options first; TASK-4408..4412 reference it for their `JsonSerializer.Serialize` call. Each mapper implements `IAuditProjectionMapper<T>` with `Map(@event, ctx) → AuditProjectionRowData`. All TENANT_TARGETED. |
| **Wiring** | `src/Backend/StatsTid.Backend.Api/Program.cs` — 6 lines `services.AddSingleton<IAuditProjectionMapper<T>, TMapper>()` + 6 lines `services.AddSingleton(new RegisteredAuditEventType(typeof(T), nameof(T)))` |
| **Validation** | `dotnet build` clean; mapper xmldoc cites ADR-026 D2 contract; uses SHIPPED signature (Map, DateTimeOffset, string DetailsJson) |

### Phase 4 — Endpoint cutover (TASK-4413)

#### TASK-4413 — 6 AdminEndpoints.cs cutover sites
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Builder Agent (one mechanical conversion per site, all in same file) |
| **Components (per site)** | (1) Convert `await outbox.EnqueueAsync(conn, tx, ...)` → `var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, ...)`. (2) Add `IAuditProjectionMapper<T> mapper` + `AuditProjectionRepository auditRepo` parameters to the endpoint lambda (Minimal API parameter binding). (3) **`ActorContext` sourcing** per Step 0b cycle 1 Codex W1 + Reviewer NOTE-1 absorption: ActorContext is NOT DI-registered and has no Minimal API binder — endpoint already binds `HttpContext context` and reads `var actor = context.GetActorContext();` (proven pattern at AdminEndpoints.cs:177-187 PUT /organizations + L120 BEGIN tx pattern). DO NOT add `ActorContext` as a lambda parameter. (4) After outbox enqueue, add: `var ctx = new AuditProjectionContext(actor.ActorId, actor.OrgId, actor.CorrelationId, new DateTimeOffset(ev.OccurredAt)); var rowData = mapper.Map(ev, ctx); await auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx, ct);` BEFORE the existing `await tx.CommitAsync(ct)`. **`DateTimeOffset` conversion**: `new DateTimeOffset(dt)` is safe for `DateTime.UtcNow`-sourced values (Kind=Utc → TimeSpan.Zero offset automatically). All 6 in-scope events inherit DomainEventBase OccurredAt = DateTime.UtcNow; safe per Reviewer NOTE-2. |
| **Sites** | L154 (OrganizationCreated), L237 (OrganizationUpdated), L587 (UserCreated — note same endpoint also enqueues EmployeeProfileCreated@L611 + UserAgreementCodeSeeded@L622+L632; only UserCreated converted in S44; the other 2 remain `EnqueueAsync` until S44b/c reopens this file), L1033 (UserUpdated — similar; UserAgreementCodeChanged@L1163+L1173 + UserAgreementCodeSuperseded@L1186+L1202 deferred to S44b), L1429 (RoleAssignmentGranted), L1542 (RoleAssignmentRevoked) |
| **Validation** | `dotnet build` clean; existing AdminAtomicTests continue to pass |

### Phase 5 — Phase E D-tests (TASK-4414)

#### TASK-4414 — 13 Docker-gated Phase E D-tests
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Builder Agent |
| **Components** | (a) **6 happy-path D-tests** at `tests/StatsTid.Tests.Regression/PhaseE/AuditProjection{Organization,User,RoleAssignment}*Tests.cs`: invoke endpoint via WebApplicationFactory + assert audit_projection row exists with correct visibility_scope/target_org_id/actor_id/details. (b) **6 forced-rollback D-tests** using `ForcedRollbackHarness.ThrowingOutboxEnqueue` (S24 TASK-2408 precedent) with TWO design shapes per Step 0b cycle 1 Codex BLOCKER absorption: (b.i) **Single-enqueue endpoints (POST /orgs L154, PUT /orgs L237, POST role L1429, DELETE role L1542)** — throw on the (only) EnqueueAndReturnIdAsync call → tx aborts BEFORE audit insert runs → assert `COUNT(*) FROM audit_projection WHERE event_id = @id = 0` (matches S27 TimeProjectionAtomicTests.cs:80-87 precedent; proves tx-atomicity holds at the audit-insert boundary even though audit insert never executes). (b.ii) **Multi-enqueue endpoints (POST /users L587, PUT /users L1033)** — throw on the SECOND enqueue (EmployeeProfileCreated@L611 or UserAgreementCodeChanged@L1163; the S44b/c-deferred emit sites that remain `EnqueueAsync`) → tx aborts AFTER UserCreated/UserUpdated audit insert has already written → assert `COUNT(*) FROM audit_projection WHERE event_id = @userCreatedEventId = 0` (proves the audit row actually rolls back, not just "wasn't written"). The two shapes together cover both pre-insert and post-insert rollback semantics. (c) **1 QueryByOrgScopeAsync Docker-gated D-test**: seed 3 orgs + 9 audit_projection rows (3 per org spanning all 3 visibility tiers: TENANT_TARGETED + GLOBAL_TENANT_VISIBLE + GLOBAL_ADMIN_ONLY) + assert filter behavior — LocalAdmin scoped to 1 org returns subset; GlobalAdmin returns full set; non-admin returns empty. |
| **Fixture** | Each D-test class TRUNCATEs audit_projection in setup per Step 0b cycle 1 Reviewer NOTE-7 (S43 backfill startup hook would otherwise populate from pre-existing dev-state events). Mirrors S27 TimeProjectionAtomicTests fixture pattern. |
| **Validation** | All 13 D-tests pass first run; `[Trait("Category", "Docker")]` literal attribute applied to each class |

### Phase 6 — Sprint Close (TASK-4415)

#### TASK-4415 — Sprint close
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | Step 7a dual-lens (Codex + Reviewer Agent); INDEX update (S44 complete row); SPRINT-44.md status flip; **reconcile SPRINT-43.md:73 forward-pointer + init.sql:2031 comment block to reflect narrow-S44 + split** per Step 4 cycle 1 Codex NOTE-2; ROADMAP narrow-S44 progress; MEMORY entry; QUALITY.md re-grade if Audit Visibility row exists; **verify EventSerializer typeof count still = 65** per Step 0b cycle 1 Reviewer NOTE-6 (S44 adds NO new event types per ADR-026 path C design). |
| **Validation** | sprint-close-guard hook passes; both Step 7a artifacts have `verdict:` + `reviewed-against-commit:` lines; EventSerializer typeof count = 65 (unchanged) |

## Forward Pointers

- **S44b = mid-size mapper families**: Config (Agreement×5 + LocalConfig + Position×4 + WTM×4 + Entitlement×4 = 18 events) — but probably split itself into Config-A + Config-B given size; OR Period (×5) + Overtime (×3) families = 8 events; OR UserAgreementCode (×3 from S34) family. Refinement at S44b sprint open picks the partition.
- **S44c = remaining mapper families**: EmployeeProfile (×4 from S33; OQ3 resolution mappers; HR-sensitive payload redaction check at refinement) + LocalAgreementProfileChanged + non-cross-process Payroll-related = ~24 events depending on S44b shape.
- **S44f = GET endpoint + frontend**: GET /api/admin/audit endpoint + AuditLogView.tsx + cutover-dependent Phase E Test #1 (catalog ↔ DI ↔ EventSerializer parity) + Test #3 (sync-in-tx assertion) + Test #4 (per-class visibility enforcement). PayrollExportGenerated decision (emit vs delete vestigial event class) lands here.
- **S44-cross-process = dedicated 1-event sprint**: solves Payroll DbConnectionFactory introduction + atomic-tx-spanning-process-boundary architectural question holistically. Sized as plumbing sprint (matches S40-ish budget; ~8 tasks).

## Cycle-Trail Note

10th sprint slot in Phase 4e architectural surge (S38→S38b→S39→S40→S41→S41a→S42→S42a→S43→S44). S43's clean close was the first non-thrash sprint since S40. S44 cycle-1 BLOCKER risk is elevated because cutover-class work historically surfaces architectural seams (S41a + S42a precedent). Mitigations: (a) caller-census done at refinement-time; (b) 3 architectural-question rows deferred via explicit TBD markers — kept OUT of implementation code path; (c) Step 4 cycle 1 dual-lens already absorbed 1 BLOCKER + 8 WARNINGs before plan-writing; (d) narrow scope limits surface area to 6 mappers + 1 file (AdminEndpoints.cs).
