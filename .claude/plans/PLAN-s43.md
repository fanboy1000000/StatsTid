# PLAN — Sprint 43: ADR-026 Sub-Sprint 1 (Audit Visibility: Schema + Repo + Interface + Backfill)

| Field | Value |
|-------|-------|
| **Sprint** | 43 |
| **Phase** | 4e (Phase E audit visibility — ADR-026 Sub-Sprint 1) |
| **Sprint type** | Implementation (plumbing only — schema + repo + interface + backfill + 2 cutover-independent Phase E tests; no per-event mappers, no GET endpoint, no frontend) |
| **Base commit** | `b2519ea` (S42a close) |
| **Refinement** | `.claude/refinements/REFINEMENT-s43-adr026-sub-sprint-1.md` (READY post Step 4 cycle 1 absorption — 2 convergent BLOCKERs + 2 WARNINGs + NOTEs absorbed inline) |
| **Sprint open date** | 2026-05-23 |
| **Task count** | 8 (TASK-4300..4307) |
| **Customer-go-live impact** | This sprint partially advances the ROADMAP L391/L394 audit-visibility architectural-unblock commitment; Sub-Sprint 2 (S44) completes it by wiring the ~53 mapper sites + GET endpoint. |

## Sprint Goal

Lay the audit-visibility plumbing per ADR-026 D1/D2/D3/D7: `audit_projection` schema + `AuditProjectionRepository` + `IAuditProjectionMapper<T>` interface + `IAuditProjectionMapperRegistry` + `AuditProjectionBackfillService` + `audit-projection-catalog.md` initial catalog + 2 cutover-independent Phase E tests. Mirror of S40 ADR-024 Sub-Sprint 1 shape. Sub-Sprint 2 (S44) wires ~53 endpoint mapper sites + GET `/api/admin/audit` + `AuditLogView.tsx`; Sub-Sprint 3 (S45) lands D-tests #1/#3/#4.

## Step 4 Cycle 1 Absorption Summary (per refinement)

Both lenses convergent on 2 BLOCKERs caught by the new `feedback_cross_process_caller_census_required.md` discipline:

1. **`RetroactiveCorrectionRequested` cross-process surface**: emitted from Payroll.Integrations via `IEventStore.AppendAsync` not ADR-026 D2's required `(conn, tx)` + `IOutboxEnqueue.EnqueueAndReturnIdAsync` shape. **Absorbed**: catalog explicitly carries `mapper_kind: TBD-payroll-dispatch-seam` for this row; Sub-Sprint 2 settles between (i) rewriting Payroll site to (conn, tx) pattern OR (ii) narrow ADR-026 errata excluding this single event from D3. **(Step 0b cycle 1 follow-up)**: original "backfill-only inclusion" third option struck — async backfill violates ADR-026 D2's sync-in-tx contract at L73/L117-129.

2. **Backfill startup gate misread**: row-count-zero gate would prevent backfill of newly-mappable events post-Sub-Sprint 2. **Absorbed**: TASK-4304 specifies unconditional `RunAsync()` per S27 precedent + `ON CONFLICT (event_id) DO NOTHING` idempotency.

Plus mechanical absorptions: `idx_*` index naming (codebase convention); `QueryByOrgScopeAsync` deferred to Sub-Sprint 2; `IAuditProjectionMapperRegistry` impl moves to Infrastructure (SharedKernel DI-package-free); ADR-026 D3 L194 `EmploymentProfile*` typo corrected in catalog; ROADMAP citation fixed.

**The new caller-census discipline worked as designed** — Step 4 cycle 1 caught a cross-process seam BEFORE implementation, sparing a cycle of architectural thrash.

## Step 0a — Entropy Scan

| Check | Result |
|-------|--------|
| Schema migration ledger | ADR-026's S39-projection naming reads as `s43-d1-audit-projection-table` per `a0e30ed` governance |
| EventSerializer count | 65 (post-S40) — unchanged by S43 (ADR-026 path C uses existing event types, adds 0) |
| Cross-process caller census | DONE per Step 4 cycle 1 via Grep `IEventStore.AppendAsync` against `src/Integrations/**` + cross-reference against ADR-026 D3 inventory — 1 cross-process emitter found at `RetroactiveCorrectionService.cs:222` (the `RetroactiveCorrectionRequested` event); documented as `TBD-payroll-dispatch-seam` for Sub-Sprint 2. Forensic trail added per Step 0b cycle 1 NOTE. |
| ADR-026 internal contradiction | Found at Step 0b cycle 1 — L182 INCLUDE-list lists `EmployeeProfile*` (4 events) but L194 NOT-audit-relevant prose says `EmploymentProfile*` (fictive name). 4 EmployeeProfile* rows get `TBD-l194-reconciliation` marker; Sub-Sprint 2 commits ADR-026 errata OR removes from INCLUDE list. |
| Pattern references verified | S40 RoleConfigOverrideRepository (repository scaffolding); S27 ProjectionBackfillService at `src/Infrastructure/StatsTid.Infrastructure/ProjectionBackfillService.cs` (backfill SSOT pattern verified by Reviewer); S23/S27 `IOutboxEnqueue.EnqueueAndReturnIdAsync` returns `Task<long>` outbox_id |
| Pre-existing ADR-026 typo | D3 L194 `EmploymentProfile*` doesn't match EventSerializer (only `EmployeeProfile*` exists); corrected at catalog ingestion (TASK-4305) |
| Customer-go-live commitment | ROADMAP L391/L394 + ADR-026 L367 — S43 advances; S44 completes |

## Step 0b — Plan Review

**MANDATORY** per WORKFLOW.md trigger criteria — sprint touches:
- **P3** (Event sourcing — new projection write contract)
- **P7** (Security — visibility_scope CHECK enforces tenant-scoping)
- **NEW caller-census discipline** — first sprint to apply the new feedback memory

Dispatch dual-lens (Codex external + Reviewer Agent internal) on this PLAN file before Phase 1 tasks. Cycle-cap = 2 per lens.

### Cycle 1 — 2026-05-23 — ABSORBED

**Codex (3 BLOCKERs + 3 WARNINGs + 3 NOTEs)**:
- B1: `RetroactiveCorrectionRequested` "backfill-only" option violates ADR-026 D2 sync-in-tx (`ADR-026:73,117-129`; `ADR-018:469`). **ABSORBED**: option struck from PLAN+refinement; reduced to 2 options ((i) Payroll rewrite OR (ii) narrow ADR-026 errata).
- B2: TASK-4306 Test #2 idempotency vacuous pre-mappers (`0 == 0` passes if mapper/scan path is broken). **ABSORBED**: Test #2 strengthened — seeds synthetic event + registers test mapper + asserts insert-once-then-conflict via production `AuditProjectionBackfillService`.
- B3: Legacy migration safety under-validated; only greenfield init checked. **ABSORBED**: NEW Test #6 added — `AuditProjectionLegacyMigrationTests.cs`: pre-S43 baseline → apply guarded ALTER twice → assert table/indexes/CHECK/ledger present exactly once.
- W1: Refinement AC stale (mentions QueryByOrgScopeAsync + SharedKernel registry placement + blank mapper_kind). **ABSORBED**: refinement AC rewritten to match plan.
- W2: Refinement OQ#4 still describes row-count gate. **ABSORBED**: OQ#4 rewritten to unconditional startup per S27 precedent.
- W3: Phase E coverage thin ("Both tests pass" too thin). **ABSORBED**: TASK-4306 validation criteria expanded with SqlState + constraint name assertions + Docker-gated trait literal.
- N1: Scope tightness good. **ACKNOWLEDGED**.
- N2: Multi-process DI named right. **ACKNOWLEDGED** (then narrowed per Reviewer W1 — Payroll registration deferred to S44).
- N3: Cycle-trail count inconsistent (8th vs 9 labels). **ABSORBED**: corrected to 9th.

**Reviewer Agent (1 BLOCKER + 2 WARNINGs + 4 NOTEs)**:
- B1 (orthogonal to Codex BLOCKERs): ADR-026 L182 INCLUDES `EmployeeProfile*` (4 events) but L194 NOT-audit-relevant prose says `EmploymentProfile*` (fictive); "typo correction" framing papers over substantive ADR ambiguity. **ABSORBED**: 4 EmployeeProfile* rows get `mapper_kind: TBD-l194-reconciliation` marker (parallel to `TBD-payroll-dispatch-seam` precedent); Sub-Sprint 2 commits ADR-026 errata OR removes 4 rows from INCLUDE list — NOT silently corrected.
- W1: `IAuditProjectionMapperRegistry` + `AuditProjectionRepository` Payroll.Integrations DI registration premature (no Sub-Sprint 1 consumer). **ABSORBED**: registration DEFERRED to S44, parallel to dispatch-seam adjudication; Sub-Sprint 1 Backend.Api-only.
- W2: TASK-4306 Test #2 must instantiate production `AuditProjectionBackfillService` (not inline SQL replay) per S27 SSOT. **ABSORBED** (overlaps Codex B2 absorption).
- N1: Caller-census procedure trail in entropy scan. **ABSORBED**: Step 0a row expanded with Grep + cross-reference method trail.
- N2: Ledger naming asymmetry comment. **ACKNOWLEDGED** (no plan change needed; future readers will see `a0e30ed` governance commit in git log).
- N3: outbox_id NULL fallback handling. **ABSORBED**: TASK-4304 validation criteria adds log-and-skip behavior for pre-S22 events.
- N4: `[Trait("Category","Docker")]` literal attribute. **ABSORBED**: TASK-4306 validation criteria explicitly cites the post-S20 `9b512b2` convention.

**Verdict**: 4 BLOCKERs absorbed mechanically (no architectural rework required — each was a missed-fact, not a structural surprise). Divergent first-cycle findings (no BLOCKER overlap between lenses) — normal cycle-1 behavior, NOT smoke-alarm thrash pattern. Cycle 2 verification queued.

### Cycle 2 — 2026-05-23 — CLEAN

**Codex (cycle 2)**: 0 BLOCKERs + 2 WARNINGs + 1 NOTE — both WARNINGs were stale refinement prose (L87-88 vacuous Test #2 wording + L118 "tests #2+#5 don't need mappers") contradicting absorbed AC. **ABSORBED**: refinement L87-90 rewritten with Test #2 strengthened form + Test #6 added in body; L118 Assumption #7 updated to reflect Test #2 mapper requirement. Codex explicitly NOTE confirmed the 3 architectural cycle 1 absorptions (RetroactiveCorrectionRequested option reduction, Test #6 added, OQ#4 unconditional startup) present.

**Reviewer Agent (cycle 2)**: 0 BLOCKERs + 5 WARNINGs — all 5 confined to refinement body staleness (PLAN file internally consistent). Reviewer verdict verbatim: *"No new BLOCKER introduced; no smoke-alarm."* Specifically verified: (a) Codex B1 backfill-only struck from BOTH PLAN L22 + refinement L16; (b) Codex B2 Test #2 strengthened — design catches all 3 failure modes (scan/mapper/insert); (c) Codex B3 Test #6 real coverage; (d) Reviewer B1 EmployeeProfile* deferral is real (NOT covert amendment). **ABSORBED**: refinement L47 (Payroll DI removed from body), L77 (typo framing replaced with TBD-l194-reconciliation framing), L152 (Risk #4 rewritten to acknowledge the two cycle-1 seams). Reviewer WARNINGs 2+3 (L88 vacuous form + L88 missing Test #6) already cleaned up via Codex cycle 2 absorption pass.

**Verdict**: Step 0b complete. **Cycle 1 = 4 BLOCKERs (3 Codex + 1 Reviewer) all absorbed mechanically. Cycle 2 = 0 BLOCKERs + 7 WARNINGs (2 Codex + 5 Reviewer) all absorbed. NO smoke-alarm pattern — divergent cycle 1 findings converged to clean by cycle 2.** Sprint open ready.

## Architectural Constraints

_Checked at close._

- [ ] **P1** Architectural integrity — `audit_projection` is the 3rd projection table after `time_entries_projection` + `absences_projection` (S27); follows ADR-018 D13 sync-in-tx pattern
- [ ] **P3** Event sourcing — projection write rides ADR-018 D3 atomic-tx contract via `(conn, tx)` overload; event log immutable per ADR-001
- [ ] **P5** Integration isolation — backfill mirrors S27 single-source-of-truth pattern
- [ ] **P7** Security — `visibility_scope` CHECK constraint + `chk_target_org_required_when_tenant` prevent malformed projection rows; Sub-Sprint 2 GET endpoint enforces scope-by-target at query time

## Task Log

### Phase 0 — Sprint Open

#### TASK-4300 — Sprint-open plumbing
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s43.md` + `docs/sprints/SPRINT-43.md` + `docs/sprints/INDEX.md` provisional |

### Phase 1 — Schema (TASK-4301)
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docker/postgres/init.sql` (append `audit_projection` CREATE TABLE + 5 indexes with `idx_*` naming + `chk_target_org_required_when_tenant` CHECK + ledger entry `s43-d1-audit-projection-table`); guarded ALTER block at end per S30/S31/S35/S40 pattern |
| **Validation** | psql `\d audit_projection` shows 13 columns + 5 indexes + CHECK; greenfield init runs clean |

### Phase 2 — Repository (TASK-4302)
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Builder Agent (mechanical pattern application; mirror RoleConfigOverrideRepository) |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/AuditProjectionRepository.cs` with `InsertAsync(conn, tx, ...)` + `CountAsync` + `CountByEventIdAsync`; **DI register in Backend.Api Program.cs only** per Step 0b cycle 1 WARNING absorption — Payroll.Integrations registration DEFERRED to Sub-Sprint 2 dispatch-seam adjudication (no Sub-Sprint 1 Payroll consumer; premature registration would be dead weight if S44 picks the ADR-026 errata option) |
| **Validation** | `dotnet build` clean; `InsertAsync` uses ON CONFLICT (event_id) DO NOTHING |

### Phase 3 — Interface + records (TASK-4303)
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `src/SharedKernel/StatsTid.SharedKernel/Audit/` — `IAuditProjectionMapper<TEvent>` interface + `AuditProjectionContext` record + `AuditProjectionRowData` record + `AuditVisibilityScope` enum + `IAuditProjectionMapperRegistry` interface |
| **Components (Infrastructure)** | `src/Infrastructure/StatsTid.Infrastructure/Audit/AuditProjectionMapperRegistry.cs` — impl with `IServiceProvider` resolution; **DI registered in Backend.Api only** per Step 0b cycle 1 WARNING absorption (Payroll.Integrations registration deferred to S44 in parallel with repository DI) |
| **Validation** | `dotnet build` clean; SharedKernel.csproj has no new `Microsoft.Extensions.*` package refs |

### Phase 4 — Backfill seeder (TASK-4304)
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Builder Agent (mirror S27 ProjectionBackfillService) |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/AuditProjectionBackfillService.cs` (S27 SSOT pattern — same instance is the canonical entry point for console app + Backend startup hook + Phase E test #2); Backend.Api startup hook adds unconditional `RunAsync()` per S27 precedent (`Program.cs:120-136`); `tools/ProjectionBackfill/Program.cs:51` extended with `--target audit_projection` flag |
| **Validation** | Console invocation + Backend startup hook both run cleanly on greenfield DB (near-zero rows pre-Sub-Sprint-2 by design); re-run is idempotent; pre-S22 events with NULL outbox_id are logged-and-skipped (no NOT NULL constraint crash) per Step 0b cycle 1 NOTE absorption |

### Phase 5 — Catalog + Phase E (TASK-4305 + TASK-4306)

#### TASK-4305 — Audit projection catalog
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/operations/audit-projection-catalog.md` (new file; new `docs/operations/` directory) |
| **Content** | Locked 7-column markdown table per refinement; ~53 rows with `mapper_kind: TBD` (or `TBD-payroll-dispatch-seam` for `RetroactiveCorrectionRequested`, or `TBD-l194-reconciliation` for 4 `EmployeeProfile*` rows per Step 0b cycle 1 BLOCKER absorption — ADR-026 L182/L194 internal contradiction left for Sub-Sprint 2 adjudication, NOT silently corrected via "typo" framing). `event_type` cells use canonical EventSerializer typeof().Name. |
| **Validation** | Markdown table parses; row count matches ADR-026 D3 inventory (11 new + ~42 retrofit); 2 TBD-with-suffix markers (`payroll-dispatch-seam` + 4× `l194-reconciliation`) explicitly present per cycle 1 |

#### TASK-4306 — Phase E tests #2 + #5 + #6 (strengthened per Step 0b cycle 1 BLOCKERs)
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Builder Agent |
| **Test #2** | `AuditProjectionBackfillIdempotencyTests.cs` — seeds 1 synthetic audit-relevant event in events table + registers 1 inline test mapper for that event type via IAuditProjectionMapperRegistry + instantiates production `AuditProjectionBackfillService` via DI (NOT inline SQL replay) + runs backfill twice + asserts first run inserts exactly 1 row + second run conflicts no-op (final `SELECT COUNT(*) FROM audit_projection = 1`). Vacuous `0 == 0` empty-DB assertion explicitly forbidden. |
| **Test #5** | `AuditProjectionSchemaConstraintTests.cs` — INSERT with `(visibility_scope='TENANT_TARGETED', target_org_id=NULL)` fails; assert `PostgresException.SqlState == "23514"` AND `ConstraintName == "chk_target_org_required_when_tenant"`. |
| **Test #6** | `AuditProjectionLegacyMigrationTests.cs` (NEW per Step 0b cycle 1 BLOCKER #3) — pre-S43 baseline DB (drop audit_projection + remove ledger entry) → apply init.sql guarded ALTER block twice → assert table exists, all 5 indexes present, CHECK constraint named correctly, ledger entry `s43-d1-audit-projection-table` appears exactly once. |
| **Validation** | All 3 tests pass; `[Trait("Category","Docker")]` literal attribute applied to each per post-S20 `9b512b2` convention |

### Phase 6 — Sprint Close (TASK-4307)
| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | Step 7a dual-lens (Codex + Reviewer Agent); INDEX update; ROADMAP update; MEMORY entry; QUALITY.md re-grade |
| **Validation** | sprint-close-guard hook passes; both Step 7a artifacts have `verdict:` + `reviewed-against-commit:` lines |

## Forward Pointers

- **S44 = ADR-026 Sub-Sprint 2 (Cutover)**: ~53 per-event mapper implementations + `OrgScopeValidator.GetAccessibleOrgsAsync` + `GET /api/admin/audit` + `AuditLogView.tsx` + Payroll dispatch-seam resolution
- **S45 = ADR-026 Sub-Sprint 3 (D-tests)**: 3 cutover-dependent Phase E tests (#1 event-coverage + #3 sync-in-tx + #4 per-class visibility)
- **Customer-go-live** unblocked architecturally after S44 close per ROADMAP L391/L394

## Cycle-Trail Note

This is the 9th sprint slot in the ongoing Phase 4e architectural surge (S38→S38b→S39→S40→S41→S41a→S42→S42a→S43) — count corrected from "8th" per Step 0b cycle 1 NOTE absorption. ADR-024 D1+D2 cutover is SUSPENDED per S42a discipline-rollback; ADR-026 work proceeds independently because path C event-projection was designed at S38b to avoid the cross-process issues that derailed ADR-024. Step 4 cycle 1 caught one cross-process surface (RetroactiveCorrectionRequested) via the new caller-census discipline + absorbed it into Sub-Sprint 2 scope.
