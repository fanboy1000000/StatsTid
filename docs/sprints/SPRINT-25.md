# Sprint 25 — Phase 4c Part 2: D2.2 ETag/If-Match Propagation

| Field | Value |
|-------|-------|
| **Sprint** | 25 |
| **Status** | planned |
| **Start Date** | 2026-05-07 |
| **End Date** | TBD |
| **Orchestrator Approved** | no |
| **Build Verified** | no |
| **Test Verified** | no |

## Sprint Goal

Propagate S22's row-version optimistic-concurrency contract from `LocalAgreementProfileRepository`/`ConfigEndpoints` PUT exemplar across 4 admin-config surfaces (3 wired + `entitlement_configs` schema-only). Delivered as 8 atomic per-surface migrations + supporting foundation. Refinement at `.claude/refinements/REFINEMENT-s25-d2-2-etag.md` (Cycle 1+2+3 reviewed, READY).

**Cycle-cap discipline (per AGENTS.md L371/L455 + `feedback_step7a_cycle_cap_discipline.md`):** Step 0b: 2 BLOCKER-fix cycles per lens; Step 7a: same. After cycle 2 on either lens halt and prompt user.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | S22 exemplar files verified: `LocalAgreementProfileRepository.cs:645/668` (SaveProfileResult + OptimisticConcurrencyException — REUSED, not duplicated); `ConfigEndpoints.cs:514` (TryParseConcurrencyPrecondition — extracted to shared in TASK-2502); `frontend/src/lib/etag.ts` helpers; `init.sql:1278-1305` migration shape; ADR-018 D7. |
| Pattern compliance spot-check | CLEAN | Admin endpoints today have NO If-Match validation (verified by grep — only profile PUT does). This is what S25 fixes; not a violation. |
| Orphan detection | CLEAN | No unused S24 files. |
| Documentation drift | CLEAN | S24 sprint close (3728ccc) recorded Phase 4c Part 1 → COMPLETE in ROADMAP. Phase 4c Part 2 is now starting. MEMORY.md S24 entry up-to-date. |
| S24 lesson absorbed | n/a | S24's "commit Phase 1 BEFORE dispatching Phase 2 worktrees" lesson applied — Phase 1 explicitly sequential, Phase 2 worktrees only after Phase 1 commit. |
| Quality grade review | deferred | Will update at sprint-end. |

**Test baseline (post-S24):** 525 unit + 35 plain regression + 105 Docker-gated (61 pre-S24 + 23 TxContractTests + 21 ForcedRollbackHarness) + 76 frontend vitest = 741 total. S25 expected addition: ~22 Docker-gated concurrency tests + 1 migration test + ~6-12 frontend vitest, target ~770-780 total.

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — schema migrations (`s25-d2-2-version` Parts A+B); P3 event sourcing/auditability (audit-row version transition columns); P7 security/access control (admin mutating endpoints); ADR-018 D7 propagation. |
| **External Codex** | invoked 2026-05-07 — cycle 1 found 3B/3W/1N → cycle 2 absorbed via plan rewrite (this version) |
| **Internal Reviewer** | invoked 2026-05-07 — cycle 1 found 2B/4W/4N → cycle 2 absorbed via plan rewrite |
| **Cycle cap** | 2 BLOCKER-fix cycles per lens (per WORKFLOW.md / `feedback_step7a_cycle_cap_discipline.md`). Cycle 1 BLOCKERs all addressed in this rewrite. Cycle 2 re-review pending. |
| **BLOCKERs resolved before Step 1** | yes — cycle 1 (5 BLOCKERs) absorbed via plan rewrite; cycle 2 found 1 cascade BLOCKER (cross-domain agent labeling), resolved in cycle-3 edit (Phase 2 agent labels corrected to Backend API + Data Model — Test & QA agent identity stays exclusive to TASK-2508 per AGENTS.md L37); cycle 2 Reviewer 0 BLOCKERs / 2 WARNINGs / 4 NOTEs all absorbed in cycle-3 edit. **READY for Step 1 decompose.** |

### Findings (cycle 1)

**Codex (3 BLOCKERs, 3 WARNINGs, 1 NOTE):**
- B1: TASK-2501 scope violation (Data Model agent can't write `tests/**`).
- B2: TASK-2509 → TASK-2510 seam under-specified (hook return shapes + mutation signatures need explicit spec).
- B3: TASK-2505 DELETE return shape "agent's call" — must pin.
- W1: TASK-2506 math wrong (3+2+3=8, not 5+2+3=10).
- W2: TASK-2511 omits ROADMAP from scope; INDEX.md:5 stale (ADR-018 is at line 26).
- W3: KB refs cite ADR-019 before it exists.
- NOTE: 2-cycle caps for Step 0b/7a not explicitly restated.

**Reviewer (2 BLOCKERs, 4 WARNINGs, 4 NOTEs):**
- B1: TASK-2503/2504/2505 — deleting v2 mutating overloads breaks 9 ForcedRollbackHarness call sites at `*AtomicTests.cs:151/194/243/106/148/192/106/152`. Need to migrate atomic tests v2→v3 atomically with v2 deletion.
- B2: TASK-2509 phasing — frontend hooks need backend response shape (Phase 3) to integrate.
- W1: TASK-2503 SaveAgreementConfigResult lacks ArchivedId for S24 Step 7a P1 semantic.
- W2: TASK-2504 Activate 23505 needs verified endpoint catch.
- W3: TASK-2502 commit-boundary needs explicit "→ commit → THEN Phase 2 worktree" sequencing.
- W4: TASK-2511 ADR-019 outline missing audit version-transition columns from cycle-3 fold-in.

### Resolution (cycle 2 plan rewrite — this version)

**Restructured to 8 tasks** (was 12) with **atomic per-surface migration in Phase 2** (Path B). Each Phase 2 task owns repo + endpoint + atomic test files for ONE surface; v2 deletion happens atomically with caller migration in same commit. Master stays green between commits. Reviewer B1 resolved by bundling test migration into per-surface task.

- **B1 (Codex)**: TASK-2501 scope tightened to `docker/postgres/init.sql` only; migration test moved to TASK-2508 (Test & QA suite).
- **B2 (Codex) + B2 (Reviewer)**: Frontend collapsed to single TASK-2506 owning apiClient + hooks + pages end-to-end; explicit hook return shape `{ data: T; etag: string; version: number }` and mutation signature `update(id, ifMatch: string, body): Promise<{ data; etag }>`.
- **B3 (Codex)**: WageTypeMapping DELETE return contract pinned in TASK-2505 — `DeleteAsync(conn, tx, ..., long expectedVersion, ct) → Task<bool>`; endpoint returns 204 No Content (NO ETag header — resource gone), 404 on not-found, 412 on stale.
- **B1 (Reviewer)**: Phase 2 task per surface includes atomic test migration in same commit.
- W1 (Codex), W1 (Reviewer): SaveAgreementConfigResult shape clarified in TASK-2502 (carries `ArchivedId Guid?` for publish-path).
- W2 (Codex): TASK-2507 (ADR-019) gains ROADMAP.md in scope; INDEX.md ref corrected.
- W2 (Reviewer): TASK-2503 includes explicit verification of existing 23505 catch in `PositionOverrideEndpoints.cs:251` activate handler before adding v3.
- W3 (Codex): KB refs marked "ADR-019 (pending — written in TASK-2507)".
- W3 (Reviewer): Phase Ordering section makes Phase 1 → commit → Phase 2 explicit.
- W4 (Reviewer): TASK-2507 ADR-019 outline = 8 decision points (per-surface SaveResult, 412/428, end-exclusive effective_to, banner-with-retry, two ETag transport patterns coexisting, 23505 distinction, v2/v3 disposition rule, **audit version-transition columns**).
- NOTE (Codex): cycle cap text added to plan header section.

## Architectural Constraints Verified

- [ ] P1 — Architectural integrity preserved (S22 atomic-outbox + ADR-018 D7 row-version pattern propagated; CreateAsync v2 + AppendAuditAsync v2 atomic-outbox primitives preserved per S24 ForcedRollback dependency)
- [ ] P3 — Event sourcing append-only semantics respected; audit rows now record version transition pair (`version_before` + `version_after`) for replay determinism
- [ ] P5 — Integration isolation and delivery guarantees (endpoint contracts strengthened with explicit If-Match precondition)
- [ ] P7 — Security and access control (admin mutating endpoints require GlobalAdminOnly + If-Match)
- [ ] P8 — CI/CD enforcement (build clean, all tests pass, +22+1 D-tests + ~6-12 frontend tests)
- [ ] P9 — Usability and UX (banner-with-retry 412 matches ProfileEditor precedent for cross-page consistency)

Not directly affected: P2, P4, P6.

## Task Log

### TASK-2501 — Schema migration `s25-d2-2-version` (Parts A + B)

| Field | Value |
|-------|-------|
| **ID** | TASK-2501 |
| **Status** | planned |
| **Agent** | Data Model (extended into Infrastructure / SQL, cross-domain authorized; **scope strictly: `docker/postgres/init.sql` ONLY**) |
| **Components** | PostgreSQL schema |
| **KB Refs** | ADR-018 D7 (row-version), ADR-019 (pending — written in TASK-2507) |
| **Phase** | Phase 1 (independent foundation; sequential — runs first) |

**Description**: Add idempotent `schema_migrations` ledger entry `s25-d2-2-version` mirroring S22's `s22-d7-d8-d9` shape (`init.sql:1278-1305`).

**Part A — row-version columns**: `ALTER TABLE ... ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1` on:
- `agreement_configs`
- `position_override_configs`
- `wage_type_mappings`
- `entitlement_configs` (schema-only per Q1; inline comment cites ADR-019 deferral)

**Part B — audit version-transition columns**: `ALTER TABLE ... ADD COLUMN IF NOT EXISTS version_before BIGINT NULL, ADD COLUMN IF NOT EXISTS version_after BIGINT NULL` on:
- `agreement_config_audit`
- `position_override_config_audit`
- `wage_type_mapping_audit`

Nullable — old audit rows + ForcedRollbackHarness v2 audit calls leave them NULL; v3 audit overload populates them. NULL-on-old-rows; no backfill needed since old audit rows pre-date row-version.

**Validation Criteria**:
- [ ] `s25-d2-2-version` migration ledger entry exists; idempotent re-runs no-op
- [ ] All 4 target tables have `version BIGINT NOT NULL DEFAULT 1`
- [ ] All 3 audit tables have `version_before BIGINT NULL` + `version_after BIGINT NULL`
- [ ] `entitlement_configs` carries inline ADR-019 deferral comment

**Files Changed** (anticipated):
- `docker/postgres/init.sql`

**Migration test** is in TASK-2508 (Test & QA, scoped to `tests/**`).

---

### TASK-2502 — Foundation: SaveResult records + entity Version property + helper extraction

| Field | Value |
|-------|-------|
| **ID** | TASK-2502 |
| **Status** | planned |
| **Agent** | Data Model (extended into Infrastructure + Backend, cross-domain authorized; scope: `src/SharedKernel/StatsTid.SharedKernel/Models/`, `src/Infrastructure/StatsTid.Infrastructure/`, `src/Backend/StatsTid.Backend.Api/Endpoints/Helpers/`, `src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs`) |
| **Components** | SharedKernel.Models / Infrastructure / Backend.Api/Endpoints/Helpers |
| **KB Refs** | ADR-018 D7, ADR-019 (pending — written in TASK-2507) |
| **Phase** | Phase 1 (sequential — runs after TASK-2501 commits, before Phase 2 dispatch) |

**Description**: Three sub-deliverables, atomic per task:

**(a) Per-surface SaveResult records** — co-located in `StatsTid.Infrastructure` (or wherever existing `SaveProfileResult` lives at `LocalAgreementProfileRepository.cs:645` — agent verifies):
- `SaveAgreementConfigResult(AgreementConfigEntity Config, long Version, bool IsCreated, Guid? ArchivedId)` — `ArchivedId` carries the prior-ACTIVE config_id for publish-path event payload (S24 Step 7a P1 semantic preserved; concurrent-status-change now manifests as `OptimisticConcurrencyException` 412)
- `SavePositionOverrideResult(PositionOverrideConfigEntity Override, long Version, bool IsCreated, string Status)` — Status field reflects ACTIVE/INACTIVE for state transitions
- `SaveWageTypeMappingResult(WageTypeMapping Mapping, long Version, bool IsCreated)`

**(b) Entity Version property** added to:
- `AgreementConfigEntity` — `public required long Version { get; init; }`
- `PositionOverrideConfigEntity` — same
- `WageTypeMapping` — same

Mirrors `LocalAgreementProfile.Version` (`Models/LocalAgreementProfile.cs:41`); `Version` (long) coexists with `OkVersion` (string) per S22 convention. **`EntitlementConfig` does NOT get the property** (schema-only deferral per Q1).

**(c) Helper extraction** — `TryParseConcurrencyPrecondition` lifted from `ConfigEndpoints.cs:514` (`private static`) to NEW shared helper at `src/Backend/StatsTid.Backend.Api/Endpoints/Helpers/EtagHeaderHelper.cs`. Helper grows admin-strict mode: two methods (or single method with `mode` parameter):
- `TryParseIfMatchOrIfNoneMatchStar(...)` — profile-flexible mode for ConfigEndpoints PUT (accepts both branches)
- `TryParseIfMatch(...)` — admin-strict mode for new admin endpoints (rejects If-None-Match: *)

ConfigEndpoints PUT continues to call helper in flexible mode — **behavior unchanged at the helper-call seam** (signature changes per the new method names; result identical).

**REUSED (NOT introduced)**:
- `OptimisticConcurrencyException` — already at `LocalAgreementProfileRepository.cs:668`
- `SaveProfileResult` — already at `LocalAgreementProfileRepository.cs:645`

**Validation Criteria**:
- [ ] 3 per-surface SaveResult records created
- [ ] 3 entity models gain `Version` property; `EntitlementConfig` does NOT
- [ ] `EtagHeaderHelper.cs` created; 2 methods (or 1 with mode param) supporting profile-flexible vs admin-strict
- [ ] `ConfigEndpoints.cs:514` updated to call helper from new location with flexible mode; PUT handler behavior unchanged at the helper-call seam
- [ ] All 21 S24 ForcedRollbackHarness tests still pass post-extraction (regression check)
- [ ] All 23 S24 TxContractTests still pass
- [ ] `dotnet build` clean (0/0)

**Files Changed** (anticipated):
- `src/SharedKernel/StatsTid.SharedKernel/Models/AgreementConfigEntity.cs`
- `src/SharedKernel/StatsTid.SharedKernel/Models/PositionOverrideConfigEntity.cs`
- `src/SharedKernel/StatsTid.SharedKernel/Models/WageTypeMapping.cs`
- 3 new SaveResult files (or co-located with SaveProfileResult)
- `src/Backend/StatsTid.Backend.Api/Endpoints/Helpers/EtagHeaderHelper.cs` (new, first file in new directory)
- `src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs` (helper call updated to use flexible mode)

---

### TASK-2503 — AgreementConfig atomic per-surface migration

| Field | Value |
|-------|-------|
| **ID** | TASK-2503 |
| **Status** | planned |
| **Agent** | Backend API + Data Model (cross-domain authorized — atomic per-surface migration; scope explicitly authorized to include named atomic test file for v2→v3 caller migration: `src/Infrastructure/StatsTid.Infrastructure/AgreementConfigRepository.cs`, `src/Backend/StatsTid.Backend.Api/Endpoints/AgreementConfigEndpoints.cs`, `tests/StatsTid.Tests.Regression/Outbox/AgreementConfigAtomicTests.cs`. NOTE: This is a cross-domain implementation task touching test files via scope authorization — distinct from the Test & QA Agent's "run after impl" identity constraint per AGENTS.md L37, which applies to TASK-2508.) |
| **Components** | Infrastructure (repo) + Backend.Api (endpoints) + Tests (atomic test migration) |
| **KB Refs** | ADR-018 D7, ADR-019 (pending) |
| **Phase** | Phase 2 (parallelizable with TASK-2504 + TASK-2505 via worktrees; depends on Phase 1 commit) |

**Description**: Single atomic per-surface migration. ALL changes for AgreementConfig surface land in ONE worktree commit.

**Part A — Repo (AgreementConfigRepository.cs)**:
- ADD v3 overloads:
  - `UpdateDraftAsync(conn, tx, configId, long expectedVersion, AgreementConfigEntity updated, ct) → Task<SaveAgreementConfigResult>`
  - `PublishAsync(conn, tx, configId, long expectedVersion, string actorId, ct) → Task<SaveAgreementConfigResult>` — concurrent-status-change manifests as `OptimisticConcurrencyException` (replaces S24 Step 7a `(Guid? ArchivedId, bool Published)` tuple); `ArchivedId` lives on SaveResult for event-payload consumer
  - `ArchiveAsync(conn, tx, configId, long expectedVersion, string actorId, ct) → Task<SaveAgreementConfigResult>`
  - `AppendAuditAsync(conn, tx, ..., long? versionBefore, long versionAfter, ct)` — NEW v3 audit overload writing version-transition pair
- DELETE v2 mutating overloads:
  - `UpdateDraftAsync(conn, tx, ...)` v2
  - `PublishAsync(conn, tx, ...)` v2
  - `ArchiveAsync(conn, tx, ...)` v2
- KEEP v2 (atomic-outbox primitives — required by ForcedRollbackHarness):
  - `CreateAsync(conn, tx, ...)` v2
  - `AppendAuditAsync(conn, tx, ...)` v2
- Self-managed v1 overloads unchanged

**Part B — Endpoint (AgreementConfigEndpoints.cs)**:
- Convert 3 mutating endpoints to v3 + If-Match:
  - L233 PUT `/api/agreement-configs/{id}` (Update DRAFT)
  - L300 POST `/api/agreement-configs/{id}/publish`
  - L385 POST `/api/agreement-configs/{id}/archive`
- For each: read `If-Match` via `EtagHeaderHelper.TryParseIfMatch` (admin-strict) → pass `expectedVersion` to v3 repo → handle `OptimisticConcurrencyException` → 412 with `{ expectedVersion, actualVersion, currentState }` body → set `ETag: "<version>"` on response. Missing If-Match → 428 Precondition Required.
- 2 Create endpoints (L71 POST create, L127 POST clone): set `ETag: "<1>"` on 201 response only; do NOT parse If-* header.
- GET-by-id endpoints (L43, L58): set `ETag` header. List endpoint (L17): include `version` field per row in body.

**Part C — Atomic test (AgreementConfigAtomicTests.cs)**:
- Migrate 3 v2 mutating call sites at L151 (UpdateDraftAsync), L194 (PublishAsync), L243 (ArchiveAsync) to v3 signatures with `expectedVersion` parameter (read from row before mutation).
- Existing assertions unchanged.

**Endpoint count check (3 mutating + 2 create + 3 GET = 8 endpoints total in file)**:
- [ ] All 3 mutating endpoints validate If-Match per Q4 spec (412/428 split)
- [ ] All 5 mutating-or-create endpoints set ETag on response
- [ ] 3 GET endpoints (by-id, by-code+version, list) include ETag/version
- [ ] All 8 endpoints retain `RequireAuthorization("GlobalAdminOnly")`

**Validation Criteria**:
- [ ] 4 v3 overloads added (UpdateDraft, Publish, Archive, AppendAudit-with-version-pair)
- [ ] 3 v2 mutating overloads deleted; CreateAsync v2 + AppendAuditAsync v2 PRESERVED
- [ ] 3 mutating endpoints validate If-Match; stale → 412, missing → 428
- [ ] 5 mutating-or-create endpoints set `ETag` header
- [ ] 3 atomic test sites migrated to v3 signatures; AgreementConfig ForcedRollbackHarness tests still pass
- [ ] No leaked control flow
- [ ] `dotnet build` clean (0/0)

**Files Changed** (anticipated, 3 files):
- `src/Infrastructure/StatsTid.Infrastructure/AgreementConfigRepository.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/AgreementConfigEndpoints.cs`
- `tests/StatsTid.Tests.Regression/Outbox/AgreementConfigAtomicTests.cs`

---

### TASK-2504 — PositionOverride atomic per-surface migration

| Field | Value |
|-------|-------|
| **ID** | TASK-2504 |
| **Status** | planned |
| **Agent** | Backend API + Data Model (cross-domain authorized; scope explicitly authorized to include named atomic test file for v2→v3 caller migration: `src/Infrastructure/StatsTid.Infrastructure/PositionOverrideRepository.cs`, `src/Backend/StatsTid.Backend.Api/Endpoints/PositionOverrideEndpoints.cs`, `tests/StatsTid.Tests.Regression/Outbox/PositionOverrideAtomicTests.cs`) |
| **Components** | Infrastructure + Backend.Api + Tests |
| **KB Refs** | ADR-018 D7, ADR-019 (pending) |
| **Phase** | Phase 2 (parallelizable; depends on Phase 1 commit) |

**Description**: Same atomic per-surface migration shape as TASK-2503.

**Part A — Repo**: ADD v3 (UpdateAsync, ActivateAsync, DeactivateAsync, AppendAudit-with-version-pair). DELETE v2 mutating (3 methods). KEEP v2 CreateAsync + AppendAuditAsync.

**Part B — Endpoint** (3 mutating + 1 create + 3 GET = 7 endpoints):
- L124 PUT update, L198 POST deactivate, L251 POST activate — If-Match required
- L62 POST create — set ETag on 201 only
- GET endpoints set ETag/version

**ActivateAsync 23505 race** (Reviewer W2): **MUST add explicit `catch (PostgresException ex when ex.SqlState == "23505")` mapping to 409** in the v3 ActivateAsync endpoint conversion. Verified at refinement time: NO existing try/catch for PostgresException 23505 in `PositionOverrideEndpoints.cs:251` today; the current 409 path comes from `ActivateAsync` returning false on row-count check (i.e., row not found), NOT from exception catching. Code comment documents 23505 → 409 stays SEPARATE from row-version 412 (different race classes; partial-unique-index `WHERE status='ACTIVE'` enforces "at most one ACTIVE per (agreement, ok, position)").

**Part C — Atomic test**: migrate 3 v2 sites at PositionOverrideAtomicTests.cs:106 (UpdateAsync), L148 (DeactivateAsync), L192 (ActivateAsync) to v3.

**Validation Criteria**:
- [ ] 4 v3 overloads added
- [ ] 3 v2 mutating overloads deleted; CreateAsync v2 + AppendAuditAsync v2 PRESERVED
- [ ] 3 mutating endpoints validate If-Match (412/428 split); 1 Create sets ETag
- [ ] 23505 → 409 explicit catch on Activate; code comment documents distinction from 412
- [ ] 3 atomic test sites migrated
- [ ] `dotnet build` clean

**Files Changed** (3 files):
- `src/Infrastructure/StatsTid.Infrastructure/PositionOverrideRepository.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/PositionOverrideEndpoints.cs`
- `tests/StatsTid.Tests.Regression/Outbox/PositionOverrideAtomicTests.cs`

---

### TASK-2505 — WageTypeMapping atomic per-surface migration

| Field | Value |
|-------|-------|
| **ID** | TASK-2505 |
| **Status** | planned |
| **Agent** | Backend API + Data Model (cross-domain authorized; scope explicitly authorized to include named atomic test file for v2→v3 caller migration: `src/Infrastructure/StatsTid.Infrastructure/WageTypeMappingRepository.cs`, `src/Backend/StatsTid.Backend.Api/Endpoints/WageTypeMappingEndpoints.cs`, `tests/StatsTid.Tests.Regression/Outbox/WageTypeMappingAtomicTests.cs`) |
| **Components** | Infrastructure + Backend.Api + Tests |
| **KB Refs** | ADR-018 D7, ADR-019 (pending) |
| **Phase** | Phase 2 (parallelizable; depends on Phase 1 commit) |

**Description**: Same shape. Special case: **DELETE return contract pinned**.

**Part A — Repo**:
- ADD v3 overloads:
  - `UpdateAsync(conn, tx, mapping, long expectedVersion, ct) → Task<SaveWageTypeMappingResult>`
  - `DeleteAsync(conn, tx, ..., long expectedVersion, ct) → Task<bool>` — returns `true` if deleted, `false` if not found. **NOT a SaveResult** (no entity to wrap post-delete).
  - `AppendAuditAsync(conn, tx, ..., long? versionBefore, long versionAfter, ct)` — v3 audit overload
- DELETE v2 mutating (UpdateAsync, DeleteAsync). KEEP v2 CreateAsync + AppendAuditAsync.

**Part B — Endpoint** (2 mutating + 1 create + 2 GET-shaped = 5 endpoints; **NO GET-by-id** — wage_type_mappings has only list + by-agreement collection reads):
- L119 PUT update — If-Match required, sets ETag on 200
- L201 DELETE — If-Match required; **on success returns 204 No Content with NO ETag header** (resource gone; nothing to ETag); 404 on not-found; 412 on stale
- L47 POST create — sets ETag on 201; no If-* parsing
- List endpoints (L17, L28): include `version` field per row in body (since no GET-by-id, frontend must compose If-Match from list response)

**Part C — Atomic test**: migrate 2 v2 sites at WageTypeMappingAtomicTests.cs:106 (UpdateAsync), L152 (DeleteAsync) to v3.

**Validation Criteria**:
- [ ] 3 v3 overloads added (Update, Delete, AppendAudit-with-version-pair)
- [ ] 2 v2 mutating overloads deleted; CreateAsync v2 + AppendAuditAsync v2 PRESERVED
- [ ] PUT validates If-Match → 412/428/ETag on 200
- [ ] DELETE validates If-Match → 204 (no ETag), 404, or 412
- [ ] POST create sets ETag on 201
- [ ] List + by-agreement responses include `version` per row
- [ ] 2 atomic test sites migrated
- [ ] `dotnet build` clean

**Files Changed** (3 files):
- `src/Infrastructure/StatsTid.Infrastructure/WageTypeMappingRepository.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/WageTypeMappingEndpoints.cs`
- `tests/StatsTid.Tests.Regression/Outbox/WageTypeMappingAtomicTests.cs`

---

### TASK-2506 — Frontend ETag wiring (apiClient + hooks + 4 admin pages)

| Field | Value |
|-------|-------|
| **ID** | TASK-2506 |
| **Status** | planned |
| **Agent** | UX (scope: `frontend/src/lib/api.ts`, `frontend/src/hooks/useAgreementConfigs.ts`, `frontend/src/hooks/usePositionOverrides.ts`, `frontend/src/hooks/useWageTypeMappings.ts`, 4 admin page files in `frontend/src/pages/admin/`, related vitest) |
| **Components** | Frontend |
| **KB Refs** | ADR-019 (pending — written in TASK-2507) |
| **Phase** | Phase 3 (depends on Phase 2 — endpoints expose ETag/version contract) |

**Description**: End-to-end frontend ETag wiring. Owned by single agent for seam coherence.

**Sub-deliverables**:

**(a) `apiClient` extension** — `frontend/src/lib/api.ts` gains a header-aware variant. Explicit return shape:
```ts
type ApiResponseWithEtag<T> = { data: T; etag: string | null; status: number }
function apiFetchWithEtag<T>(url: string, init?: RequestInit): Promise<ApiResponseWithEtag<T>>
```
Existing `apiClient` callers stay unchanged. **Reuse existing `frontend/src/lib/etag.ts` helpers** (`parseVersionFromETag`, `formatVersionAsIfMatch`, `resolveEtag` from S22+S23) — apiFetchWithEtag should consume `parseVersionFromETag` for parity with S23 weak-ETag handling rather than re-implementing. **Sibling-module pattern divergence** (cycle-2 W3): `frontend/src/api/profileApi.ts` (NOTE: actual path — at `src/api/`, not `src/lib/`) is NOT migrated to the new variant — stays as legacy S22 sibling-module pattern; documented in ADR-019.

**(b) Admin hook return shapes + mutation signatures** (Codex B2 spec):
```ts
type WithEtag<T> = T & { etag: string; version: number }

useAgreementConfigs(): {
  configs: WithEtag<AgreementConfig>[]
  updateConfig: (id: string, ifMatch: string, body: ConfigUpdate) => Promise<WithEtag<AgreementConfig>>
  publishConfig: (id: string, ifMatch: string) => Promise<WithEtag<AgreementConfig>>
  archiveConfig: (id: string, ifMatch: string) => Promise<WithEtag<AgreementConfig>>
  // ... etc
}

usePositionOverrides(): { overrides: WithEtag<PositionOverride>[]; ... }
useWageTypeMappings(): { mappings: WithEtag<WageTypeMapping>[]; ... }
```

Each mutation reads `etag` from list response (or per-row `version`) and threads as `ifMatch` parameter. 412 errors expose `{ expected: number; actual: number }` for banner display.

**(c) Banner-with-retry 412 UX on 4 admin pages** — match `frontend/src/components/config/ProfileEditor.tsx` precedent (`:135` `staleConflict` state declaration; `:213-220` 412 handler logic; `:283-289` banner JSX). Each page uses `useState<{ expected?: number; actual?: number }>` for `staleConflict`; banner offers "Refresh and lose changes" button that re-fetches list then clears banner.

Pages:
- `frontend/src/pages/admin/AgreementConfigList.tsx` — list-row state transitions (publish/archive)
- `frontend/src/pages/admin/AgreementConfigEditor.tsx` — DRAFT update editor
- `frontend/src/pages/admin/PositionOverrideManagement.tsx` — update + activate/deactivate
- `frontend/src/pages/admin/WageTypeMappingManagement.tsx` — update + delete

**Validation Criteria**:
- [ ] `apiClient` extension: 1 new exported function with return type `ApiResponseWithEtag<T>`; existing callers unchanged
- [ ] 3 admin hooks return shapes carry `WithEtag<T>` per row; mutation methods accept `ifMatch: string`
- [ ] 4 admin pages handle 412 with banner-with-retry UX matching ProfileEditor precedent
- [ ] `profileApi.ts` unchanged (legacy sibling-module pattern preserved per ADR-019)
- [ ] All existing 76 frontend vitest tests still pass
- [ ] ~6-12 new frontend vitest tests added (apiClient extension + banner-with-retry on 1-2 representative pages + hook mutation signature)
- [ ] Manual smoke: open dev server, exercise one mutating action per page, verify ETag flow + 412 banner
- [ ] **Strict-shape spot-check (Reviewer W2 absorption)**: grep across 4 admin pages for `Object.keys(...)`, manual `JSON.stringify` of full row, or shape assertions; confirm `WithEtag<T>` extra `etag`/`version` fields don't leak into UI logic or break shape-strict consumers
- [ ] **MANDATORY Reviewer audit at task close** (Reviewer NOTE absorption — large UX scope: 1 lib + 3 hooks + 4 pages + tests = largest UX task in recent sprints): Reviewer Agent invoked to verify all 4 pages consistently use banner-with-retry (not just 1-2 representatives covered by tests)

**Files Changed** (anticipated):
- `frontend/src/lib/api.ts` + `frontend/src/lib/api.test.ts`
- 3 admin hook files
- 4 admin page files
- New vitest tests for banner-with-retry on 1-2 representative pages

---

### TASK-2507 — ADR-019 + INDEX.md + ROADMAP

| Field | Value |
|-------|-------|
| **ID** | TASK-2507 |
| **Status** | planned |
| **Agent** | Orchestrator-only (scope: `docs/knowledge-base/decisions/`, `docs/knowledge-base/INDEX.md`, `ROADMAP.md`) |
| **Components** | Knowledge Base / Roadmap |
| **KB Refs** | NEW ADR-019 |
| **Phase** | Phase 3 (parallelizable with TASK-2506) |

**Description**: Write `ADR-019-optimistic-concurrency-via-row-version.md`. Cross-references ADR-018 D7 + ADR-017 D2. **8 decision points** (canonical):

1. Per-surface SaveResult convention (`SaveAgreementConfigResult`, `SavePositionOverrideResult`, `SaveWageTypeMappingResult` mirror S22's `SaveProfileResult`)
2. 412 (stale `If-Match`) vs 428 (missing `If-Match`) split per RFC 6585
3. End-exclusive `effective_to` semantics where applicable (agreement_configs ARCHIVED transition; not applicable to position_overrides flat-CRUD)
4. Banner-with-retry frontend UX precedent (matches `ProfileEditor.tsx:213-220`)
5. Two ETag transport patterns coexisting: `apiClient` extension at `frontend/src/lib/api.ts` for admin pages (S25); sibling-module pattern at `frontend/src/api/profileApi.ts` for profile (S22 legacy, kept compatibly)
6. 23505 unique-violation distinction from row-version 412 (position_override Activate)
7. v2-vs-v3 disposition rule: delete v2 mutating-with-concurrency methods; preserve v2 atomic-outbox primitives (CreateAsync + AppendAuditAsync) per S24 ForcedRollback dependency
8. **Audit version-transition columns** (`version_before` + `version_after` on 3 audit tables) — closes audit-replay gap

Update `docs/knowledge-base/INDEX.md` to add ADR-019 row at the appropriate location (current ADR-018 row at L26 — append below).

Update `ROADMAP.md` to mark Phase 4c Part 2 → COMPLETE upon S25 close (this happens at sprint close, not in TASK-2507; TASK-2507 only writes ADR + INDEX. ROADMAP update is in sprint close commit).

**Validation Criteria**:
- [ ] ADR-019 file created with all 8 decision points
- [ ] Cross-references ADR-018 D7 + ADR-017 D2
- [ ] INDEX.md updated with ADR-019 row (correct line — append below current ADR-018 row at L26)

**Files Changed**:
- `docs/knowledge-base/decisions/ADR-019-optimistic-concurrency-via-row-version.md` (new)
- `docs/knowledge-base/INDEX.md`

(ROADMAP.md update happens at sprint close, not in this task.)

---

### TASK-2508 — Concurrency test suite + migration test

| Field | Value |
|-------|-------|
| **ID** | TASK-2508 |
| **Status** | planned |
| **Agent** | Test & QA (scope: `tests/**` per AGENTS.md L37) |
| **Components** | Tests / Regression |
| **KB Refs** | ADR-018 D7, ADR-019 |
| **Phase** | Phase 4 (sequential — runs AFTER all impl per AGENTS.md L37) |

**Description**: Two test suites:

**(a) Migration test** (absorbed from cycle-2 fix of TASK-2501 scope violation):
- 1 Docker-gated test asserting `init.sql` re-run is idempotent; `version` columns backfill to 1; new audit rows can carry NULL or numeric in `version_before`/`version_after`.

**(b) Concurrency test suite** (~22 tests):
- 8 stale-If-Match → 412 tests (one per converted mutating endpoint: PUT update + state transitions for agreement-config; PUT + activate/deactivate for position-override; PUT + DELETE for wage-type-mapping)
- 8 missing-If-Match → 428 tests (one per converted mutating endpoint)
- 3 end-to-end ETag-cycle tests (one per surface: GET → If-Match on mutation → ETag in response)
- 3 audit version-transition tests (one per surface): assert v3 audit row has `version_before` + `version_after` populated; v2 audit row has them NULL
- Tests use direct orchestration mirroring per `ProfileAuditTests` precedent (matches S24 TASK-2408 convention; HTTP-surface harness deferred to Phase 4d)

**Total target: ~22 + 1 = 23 new D-tests.**

**Validation Criteria**:
- [ ] Migration test compiles + asserts idempotency
- [ ] ~22 concurrency tests added under `tests/StatsTid.Tests.Regression/Concurrency/` (or similar)
- [ ] Each test marked `[Trait("Category", "Docker")]`
- [ ] All tests compile clean (Docker runtime gating same as S24)
- [ ] Existing 21 ForcedRollbackHarness tests still pass (regression check for v2 atomic-outbox preservation)
- [ ] Existing 23 TxContractTests still pass

**Files Changed** (anticipated):
- `tests/StatsTid.Tests.Regression/Migrations/S25VersionMigrationTests.cs` (new)
- `tests/StatsTid.Tests.Regression/Concurrency/AgreementConfigConcurrencyTests.cs` (new)
- `tests/StatsTid.Tests.Regression/Concurrency/PositionOverrideConcurrencyTests.cs` (new)
- `tests/StatsTid.Tests.Regression/Concurrency/WageTypeMappingConcurrencyTests.cs` (new)
- `tests/StatsTid.Tests.Regression/Concurrency/AuditVersionTransitionTests.cs` (new)

---

## Phase Ordering

**Phase 1 (sequential — single-agent steps, commit between each)**:
- TASK-2501 schema migration → commit
- TASK-2502 records/entity/helper → commit

**Critical**: commit Phase 1 (TASK-2501 + TASK-2502) BEFORE dispatching Phase 2 worktrees. S24 lesson absorbed.

**Phase 2 (parallel via worktrees — atomic per-surface migration)**:
- TASK-2503 AgreementConfig (repo + endpoint + atomic test in 1 commit)
- TASK-2504 PositionOverride (repo + endpoint + atomic test in 1 commit)
- TASK-2505 WageTypeMapping (repo + endpoint + atomic test in 1 commit)

Each worktree touches 3 disjoint files; zero merge-conflict surface across worktrees. Master green between commits.

**Phase 3 (parallel)**:
- TASK-2506 Frontend (single agent owns end-to-end seam)
- TASK-2507 ADR-019 + INDEX

**Phase 4 (sequential, runs AFTER all impl per AGENTS.md L37)**:
- TASK-2508 Test & QA (concurrency + migration tests)

**Phase 5 (Orchestrator)**: build/test validation, Constraint Validator on each agent output, Reviewer audits per task (P3 + P7 trigger MANDATORY), Step 7a Codex review on full sprint diff.

**Cycle cap discipline**: 2 BLOCKER-fix cycles per lens at Step 0b and Step 7a. After cycle 2 on either lens, halt and prompt user (per WORKFLOW.md / `feedback_step7a_cycle_cap_discipline.md`).

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule engine changes |
| Wage type mappings produce correct SLS codes | N/A | No payroll calculation changes |
| Overtime/supplement determinism | N/A | No rule engine changes |
| Absence effects correct | N/A | No absence logic changes |
| Retroactive recalculation stable | N/A | No retroactive logic changes |

S25 is admin-config infrastructure-only (optimistic concurrency hardening). Legal/payroll surfaces unaffected.

## External Review (Step 7a)

_Pending sprint-end._

| Field | Value |
|-------|-------|
| **Invoked** | not yet |
| **Sprint-start commit** | `3728ccc` (S24 sprint close) |
| **Command** | TBD at sprint end |
| **Review Cycles** | 0 (cycle cap: 2 per WORKFLOW.md) |
| **Findings** | 0 |
| **Resolution** | n/a |

## Test Summary

_Pending sprint-end. Target: 525 unit + 35 plain + ~128 Docker-gated (105 pre-S25 + 22 concurrency + 1 migration) + ~88 frontend vitest = ~776 total._

## Agent Effectiveness

_Pending sprint-end._

## Sprint Retrospective

_Pending sprint-end._
