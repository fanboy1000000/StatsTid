# ADR-019: Optimistic Concurrency via Row-Version (Admin-Strict Resources)

**Status**: ACCEPTED (cycles 1-3 of plan-mode review absorbed 2026-05-07; pending Step 7a Codex pass at S25 close)
**Date**: 2026-05-08
**Sprint**: 25
**Amends**: [ADR-018](ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md) D7 — propagates the row-version + If-Match contract from the `local_agreement_profiles` exemplar to the three remaining admin-strict mutating resources.
**Cross-references**: [ADR-017](ADR-017-local-agreement-configuration-as-a-profile.md) D2 (close-then-insert window math), [ADR-014](ADR-014-agreement-configs-database-backed.md) (DRAFT/ACTIVE/ARCHIVED lifecycle).

## Context

ADR-018 D7 introduced row-version optimistic concurrency on `local_agreement_profiles`: the admin pilot endpoint (`PUT /api/admin/orgs/{id}/agreements/{code}/{ok}`) writes a `BIGINT version` column on each save, exposes it as a quoted ETag (`ETag: "<n>"`), and rejects stale `If-Match` headers with 412. ADR-018 D2.2 was deferred at sprint close on the explicit understanding that the pattern needed a published exemplar before propagation.

Three admin-strict mutating resources remained outside that contract through end of S24:

| Resource | Endpoints | Concurrency exposure |
|----------|-----------|----------------------|
| `agreement_configs` | DRAFT update / publish / archive / clone | DRAFT in-place edit race; concurrent publish vs archive race |
| `position_override_configs` | update / activate / deactivate | Update collides with admin-side state transition |
| `wage_type_mappings` | update / delete | Update collides with delete; concurrent edits clobber each other |

Until S25, all three accepted writes without precondition validation: last-write-wins. The race window was small in production (single GlobalAdmin operator typical), but each resource carried at least one verified theoretical race condition surfaced during S22 cycle-2 Reviewer findings (Reviewer B1 on `agreement_configs` listed three: DRAFT in-place / DRAFT→ACTIVE publish / ACTIVE→ARCHIVED clone).

`entitlement_configs` was added to the list during S25 refinement Q1 because it shares the admin-strict mutating shape. The schema column lands in S25 but the endpoint contract migration is deferred (no current concurrency complaints; deferred to a follow-up sprint per Q1 verdict).

## Decision

We propagate ADR-018 D7's row-version + If-Match contract uniformly across the three wired admin-strict resources, with a per-surface SaveResult record carrying the structured save outcome and a shared `EtagHeaderHelper.TryParseIfMatch` helper enforcing admin-strict precondition semantics (412 stale / 428 missing). Each repository ships v3 mutating overloads that REPLACE the v2 mutating overloads from S24's atomic-outbox propagation, and atomically migrate their callers (endpoint + ForcedRollbackHarness atomic test) in the same commit. The v2 atomic-outbox primitives — `CreateAsync(conn, tx, ...)` and `AppendAuditAsync(conn, tx, ...)` without version-pair columns — are PRESERVED because S24's `ForcedRollbackHarness` consumes them as the canonical Phase-2 atomic in-tx pattern.

Audit tables gain `version_before BIGINT NULL, version_after BIGINT NULL` columns to record the full version transition pair on every CREATE/UPDATE/DELETE event. Replay determinism per ADR-016 D10 is preserved: old audit rows + v2 atomic-outbox-primitive audit calls leave both columns NULL; v3 audit overload populates them.

## Detailed Decisions

### D1 — Per-surface SaveResult convention

Each repository exposes a per-surface `SaveResult` record carrying the post-save entity, its new `version`, and whether this save was a first-creation:

```csharp
public sealed record SaveAgreementConfigResult(AgreementConfig Config, long Version, bool IsCreated, Guid? ArchivedId);
public sealed record SavePositionOverrideResult(PositionOverrideConfig Override, long Version, bool IsCreated);
public sealed record SaveWageTypeMappingResult(WageTypeMapping Mapping, long Version, bool IsCreated);
```

`SaveAgreementConfigResult` carries an additional `ArchivedId Guid?` for the publish path: when DRAFT→ACTIVE publishing supersedes an existing ACTIVE config of the same `(agreementCode, okVersion)`, the prior ACTIVE config is archived, and the publish handler must emit two audit rows + two outbox events. The `ArchivedId` field carries the prior ACTIVE id back to the endpoint so the endpoint can route the second audit row + outbox event through the same in-tx pattern. Closes S24 Step 7a P1 (publish-race fix) at the type level: the endpoint cannot accidentally drop the archive-emission when reading the result.

`DeleteAsync` for `wage_type_mappings` returns `Task<bool>` rather than a SaveResult — there is no entity to wrap once the row is gone, and the success/not-found split is sufficient signal for the endpoint to choose 204 vs 404.

### D2 — 412 stale `If-Match` vs 428 missing `If-Match`

Per RFC 6585 §4 (Required) and RFC 7232 §3.1 (If-Match): missing `If-Match` on a mutating request returns **428 Precondition Required** with body `{ error: "<helper hint>" }`. Stale `If-Match` (parses as version N where N != currentVersion) returns **412 Precondition Failed** with body `{ expectedVersion, actualVersion, currentState }`. Malformed `If-Match` (cannot parse as quoted decimal integer) returns 428 with the parse error in the helper hint.

`EtagHeaderHelper.TryParseIfMatch` is the single shared parser. It runs in admin-strict mode for all S25-propagated endpoints: `If-None-Match: *` is REJECTED (admin endpoints don't use first-creation semantics — POST Create is a separate handler). The local-agreement-profile endpoint at `ConfigEndpoints.cs:514` uses a non-admin-strict variant (`TryParseConcurrencyPrecondition`) that DOES accept `If-None-Match: *` for first-creation; the helper extraction in TASK-2502 keeps the two modes in one type with a constructor parameter.

The 412 body's `currentState` field is endpoint-specific and aids the frontend banner copy: `agreement_configs` emits the lifecycle status (`"DRAFT"` / `"ACTIVE"` / `"ARCHIVED"`); `position_override_configs` emits the activate status; `wage_type_mappings` emits `"exists"` (only state distinction is presence/absence, captured by 412 vs 404 split).

### D3 — End-exclusive `effective_to` semantics where applicable

ADR-018 D2 amended ADR-017 D2 to use end-exclusive `effective_to`: when a new profile supersedes a predecessor on the same date, predecessor `effective_to = newProfile.EffectiveFrom` (NOT `EffectiveFrom - 1`).

Of the three S25-propagated resources, only `agreement_configs` carries lifecycle effective-dating (DRAFT has no effective dates; ACTIVE carries `(effective_from, effective_to)`; ARCHIVE preserves the historical row). The end-exclusive convention is preserved on the publish path: when publishing a new ACTIVE supersedes a prior ACTIVE of the same `(agreementCode, okVersion)`, the prior ACTIVE's `effective_to` is stamped at the new ACTIVE's `effective_from`.

`position_override_configs` is flat-CRUD with `(active|inactive)` toggle — no effective-dating. `wage_type_mappings` is flat-CRUD with composite key `(time_type, ok_version, agreement_code, position)` — no effective-dating either. End-exclusive semantics do not apply to either.

### D4 — Banner-with-retry frontend UX

Each of the four S25-touched admin pages mirrors `frontend/src/components/config/ProfileEditor.tsx:135` (state declaration: `useState<{ expected?: number; actual?: number } | null>`), `:213-220` (412 handler: `if (e.status === 412) setStaleConflict({ expected: e.body?.expectedVersion, actual: e.body?.actualVersion })`), and `:283-293` (banner JSX: warning Alert with the expected/actual version pair displayed in parentheses, plus a refresh-and-lose-changes affordance).

The banner pattern matches ProfileEditor verbatim because the conflict shape is identical: another admin saved while this user was editing, and the only safe recovery path is to refresh the local view. Cross-page consistency reduces operator surprise — the same admin who edits profiles also edits agreement configs / position overrides / wage type mappings, and the conflict UX is identical across all four resources.

### D5 — Two ETag transport patterns coexisting

The frontend carries two distinct ETag-aware fetch patterns:

- **Sibling-module pattern** (S22, ADR-018 D7) — `frontend/src/api/profileApi.ts` is a hand-rolled fetch wrapper specific to the profile resource. Its `getCurrentProfile` and `saveProfile` functions extract ETag headers manually, run them through `parseVersionFromETag` / `resolveEtag`, and surface `(data, etag, version)` to callers. Used only by `useConfig.ts` and `ProfileEditor.tsx`.
- **`apiClient` extension** (S25, this ADR) — `frontend/src/lib/api.ts` gains a new export `apiFetchWithEtag<T>` alongside the existing `apiClient`. The new function is shared by all three S25-touched admin hooks. Existing `apiClient` callers stay unchanged.

The two patterns coexist intentionally. `profileApi.ts` is left as-is because (a) it works, (b) its sibling-module shape was justified at the time by the profile resource's complex GET-by-business-key semantics that don't fit the generic `apiFetchWithEtag<T>` shape, and (c) gratuitous migration would burn a second signature break on `useConfig.ts` for no behavior change. New admin-strict resources go through `apiFetchWithEtag` — see `frontend/src/hooks/useAgreementConfigs.ts`, `usePositionOverrides.ts`, `useWageTypeMappings.ts` for the canonical pattern.

Both patterns reuse the same wire-format helpers in `frontend/src/lib/etag.ts` (`parseVersionFromETag`, `formatVersionAsIfMatch`, `resolveEtag`). Format duplication is forbidden — both transports route through the same parser.

### D6 — 23505 unique-violation distinction from 412

`position_override_configs` activate handler at `PositionOverrideEndpoints.cs:251` already catches PostgresException SQL state 23505 (unique-constraint violation) on the `(agreement_code, ok_version, position_code) WHERE status = 'active'` partial-unique-index. That catch surfaces as a 409 Conflict with `{ error: "...", currentState: "another override is active" }` — a **business-rule conflict**, NOT an optimistic-concurrency conflict.

The two are distinct: 412 means "your If-Match doesn't match the current row version"; 409 means "your operation would violate a uniqueness invariant against a different row." The frontend's 412-banner-with-retry handler MUST NOT also catch 409 — they require different recovery paths (412 = "refresh and reapply"; 409 = "user error, deactivate the other row first").

S25 verifies the existing 23505 catch is preserved through the v3 migration (TASK-2504 Reviewer W2 absorption) and that 412 from the v3 path does not collide with 409 from the partial-unique-index path. The two catch blocks live in the same endpoint handler, in the order: `OptimisticConcurrencyException` (412) first, `PostgresException` with state 23505 (409) second.

### D7 — v2-vs-v3 disposition rule

S24's atomic-outbox propagation (TASK-2206) added `(NpgsqlConnection, NpgsqlTransaction)` overloads (v2) to seven repositories. S25's per-surface migration adds v3 overloads with an additional `long expectedVersion` parameter and structured return shape. Disposition rule:

| Overload | Purpose | S25 disposition |
|----------|---------|-----------------|
| v1 (self-managed connection) | Test-only scaffolding (ConfigResolutionService tests, etc.) | KEEP unchanged |
| v2 mutating (`UpdateAsync(conn, tx, entity)`, `DeleteAsync(conn, tx, ...)`) | Atomic in-tx writes without concurrency check | DELETE (replaced by v3) |
| v2 atomic-outbox primitives (`CreateAsync(conn, tx, ...)`, `AppendAuditAsync(conn, tx, ...)`) | First-create + audit append within atomic-outbox tx | KEEP — required by S24 `ForcedRollbackHarness` |
| v3 (`UpdateAsync(conn, tx, entity, expectedVersion)` etc.) | Atomic in-tx writes with row-version concurrency check | NEW — exclusive caller for mutating endpoints |

The KEEP-v2-atomic-outbox-primitives clause is non-negotiable. Removing them would break the 21 `ForcedRollbackHarness` Docker-gated regression tests added in TASK-2408, all of which exercise the S24 atomic-outbox-failure-rolls-back-state-change invariant. POST Create endpoints continue to use v2 `CreateAsync` + v2 `AppendAuditAsync` (with NULL `version_before` / `version_after` per D8 below) because first-create has no precondition and no version transition pair to record.

### D8 — Audit version-transition columns

Each of the three propagated audit tables gains:

```sql
ALTER TABLE agreement_config_audit ADD COLUMN IF NOT EXISTS version_before BIGINT NULL;
ALTER TABLE agreement_config_audit ADD COLUMN IF NOT EXISTS version_after  BIGINT NULL;
ALTER TABLE position_override_config_audit ADD COLUMN IF NOT EXISTS version_before BIGINT NULL;
ALTER TABLE position_override_config_audit ADD COLUMN IF NOT EXISTS version_after  BIGINT NULL;
ALTER TABLE wage_type_mapping_audit         ADD COLUMN IF NOT EXISTS version_before BIGINT NULL;
ALTER TABLE wage_type_mapping_audit         ADD COLUMN IF NOT EXISTS version_after  BIGINT NULL;
```

Both columns are NULLable. Population convention by event type:

| Event | `version_before` | `version_after` |
|-------|------------------|-----------------|
| CREATE | NULL | 1 (or NULL — see below) |
| UPDATE | prior `version` | new `version` |
| DELETE | `version` at deletion | `version` at deletion (NOT NULL — records the version at point of deletion since the row is gone) |
| Lifecycle transition (publish/archive) | prior `version` | new `version` |
| ForcedRollbackHarness fixture audit (v2 atomic-outbox primitive) | NULL | NULL |

CREATE-event population follows TASK-2503's AgreementConfig precedent: POST Create handlers continue to use v2 `AppendAuditAsync` (no version-pair columns) so `version_before` and `version_after` are both NULL on first-create audit rows. Rationale: first-create has no preceding version-transition pair to record, and the v3 migration does not require touching the POST handlers. TASK-2504 / TASK-2505 mirror this convention.

DELETE-event population uses `(expectedVersion, expectedVersion)` rather than NULL: the row is gone post-delete but the audit row is the only post-mortem trace of its existence; recording the version at point of deletion supports replay determinism and audit completeness. Verified by the WageTypeMapping DELETE handler in TASK-2505.

Replay determinism per ADR-016 D10 is preserved: replay against a manifest snapshot does not depend on audit version-transition columns being populated (those columns are write-only metadata; replay reads the entity's `version` directly from the row). Old audit rows from before S25's schema migration leave both columns NULL — non-blocking for any read or replay path.

## Implications

**Backend (Infrastructure + Backend.Api):**
- Three repositories gain v3 mutating overloads + v3 audit overload (`AgreementConfigRepository`, `PositionOverrideRepository`, `WageTypeMappingRepository`).
- Three repositories drop v2 mutating overloads (`UpdateAsync(conn, tx, entity)`, `DeleteAsync(conn, tx, ...)`); v2 atomic-outbox primitives (`CreateAsync(conn, tx, ...)`, `AppendAuditAsync(conn, tx, ...)`) PRESERVED.
- Six endpoint files gain admin-strict If-Match validation via shared `EtagHeaderHelper.TryParseIfMatch`: 412 / 428 / 409 (where applicable) / 204 (DELETE) split.
- One new shared helper: `StatsTid.Backend.Api.Endpoints.Helpers.EtagHeaderHelper`.
- `OptimisticConcurrencyException` REUSED from `LocalAgreementProfileRepository.cs:668` (not duplicated per-surface).

**Data Model (PostgreSQL schema):**
- One new schema_migrations entry: `s25-d2-2-version`.
- Four tables gain `version BIGINT NOT NULL DEFAULT 1` (`agreement_configs`, `position_override_configs`, `wage_type_mappings`, `entitlement_configs`).
- Three audit tables gain `version_before BIGINT NULL`, `version_after BIGINT NULL` (`agreement_config_audit`, `position_override_config_audit`, `wage_type_mapping_audit`).
- `entitlement_configs` ships schema-only — endpoint contract migration deferred to a follow-up sprint per S25 refinement Q1 verdict (no current race-condition complaints; schema is forward-compatible).

**SharedKernel:**
- Three entity classes gain `public long Version { get; init; } = 1;` (`AgreementConfig`, `PositionOverrideConfig`, `WageTypeMapping`).

**Frontend:**
- New export `apiFetchWithEtag<T>` in `frontend/src/lib/api.ts`; existing `apiClient` callers unchanged.
- Three admin hooks migrate to `WithEtag<T>` per-row shape with `ifMatch: string` mutation parameters (`useAgreementConfigs.ts`, `usePositionOverrides.ts`, `useWageTypeMappings.ts`).
- Four admin pages gain banner-with-retry 412 UX mirroring `ProfileEditor.tsx:135/213-220/283-293`.
- `frontend/src/api/profileApi.ts` UNCHANGED (legacy sibling-module pattern preserved per D5).

**Tests:**
- ~22 new Docker-gated concurrency tests (412 races, 428 missing, audit version-transition correctness across CREATE/UPDATE/DELETE × 3 resources).
- 1 migration idempotency test for `s25-d2-2-version`.
- ~6-12 new frontend vitest tests (apiClient extension + banner-with-retry on 1-2 representative pages).

## Alternatives Rejected

**Single shared `SaveAdminResourceResult<T>` record instead of per-surface records.** Rejected because the per-surface records carry resource-specific fields the generic shape cannot accommodate — `SaveAgreementConfigResult.ArchivedId Guid?` (publish-supersession case) is meaningful only for `agreement_configs`; collapsing to a generic record either drops the field (losing the S24 Step 7a P1 fix's type-level guarantee) or carries it everywhere (semantic noise on the other two surfaces). Per-surface records preserve the read-the-type-and-know-what-it-means property.

**Migrate `profileApi.ts` to `apiFetchWithEtag` for consistency.** Rejected because (a) `profileApi.ts` works in production and has been Step 7a Codex-reviewed across S22 + S23 cycles; (b) its sibling-module shape is informed by the profile resource's complex GET-by-business-key semantics that the generic `apiFetchWithEtag<T>` shape doesn't fit; (c) the migration would burn a second signature break on `useConfig.ts` for no behavior change. Coexistence is the lower-cost path.

**Defer `entitlement_configs` schema column too.** Rejected during S25 refinement Q1 in favor of schema-only (column in S25, endpoint contract migration deferred). Rationale: schema migration is cheap and forward-compatible; deferring the column too forces a second migration when the endpoint contract finally lands. Adding the column now is the low-cost forward-compatibility move.

**Use `If-Match: *` (RFC 7232 wildcard) for first-creation precondition.** Rejected for admin-strict resources because RFC 7232 specifies `If-Match: *` as "match any current representation" — useful for "create or update if exists, but don't create if absent" patterns. S25's POST Create endpoints don't carry that semantic — they always create. The local-agreement-profile resource accepts `If-None-Match: *` for first-creation (admin-non-strict mode); the three S25 resources do not (admin-strict mode). The `EtagHeaderHelper` carries both modes via constructor parameter; `TryParseIfMatch` rejects `If-None-Match: *` in admin-strict mode.

## Review Cycles

### Cycle 1 (2026-05-07) — plan-mode review

External Codex: 3 BLOCKERs, 3 WARNINGs, 1 NOTE.
- B1: TASK-2501 scope violation (Data Model agent can't write `tests/**`).
- B2: TASK-2509 → TASK-2510 seam under-specified.
- B3: TASK-2505 DELETE return shape "agent's call" — must pin.

Internal Reviewer: 2 BLOCKERs, 4 WARNINGs, 4 NOTEs.
- B1: TASK-2503/2504/2505 — deleting v2 mutating overloads breaks 9 ForcedRollbackHarness call sites at `*AtomicTests.cs`. Need atomic v2→v3 migration in same commit.
- B2: TASK-2509 phasing — frontend hooks need backend response shape (Phase 3) to integrate.

### Cycle 2 (2026-05-07) — plan rewrite

Restructured to 8 tasks with atomic per-surface migration in Phase 2 (Path B). Each Phase 2 task owns repo + endpoint + atomic test files for ONE surface; v2 mutating overloads delete atomically with caller migration in same commit. Master stays green between commits. Frontend collapsed to single TASK-2506 owning apiClient + hooks + pages end-to-end.

### Cycle 3 (2026-05-07) — cross-domain agent labeling fix

Codex cycle-2 found 1 cascade BLOCKER on Phase 2 agent labels — `Test & QA agent` was used for the Phase 2 atomic-test migration sub-deliverable but per AGENTS.md L37 the Test & QA agent identity is exclusive to the dedicated test sprint task. Cycle-3 edit corrected Phase 2 agent labels to "Backend API + Data Model (cross-domain authorized)" matching S22's TASK-2205/2206 governance precedent. Reviewer cycle 2 (0 BLOCKERs / 6 WARNINGs / 6 NOTEs) absorbed in cycle-3 edit.

### Step 7a (2026-05-08, pending)

External Codex review on full sprint diff vs S24 close (`3728ccc`) deferred to S25 sprint close per WORKFLOW.md Step 7a.

## References

- [ADR-014](ADR-014-agreement-configs-database-backed.md) — DB-backed agreement configs lifecycle; S25 propagates concurrency contract onto the lifecycle endpoints.
- [ADR-016](ADR-016-temporal-period-handling.md) — D10 replay-determinism pattern preserved (audit version-transition columns are write-only metadata, not consumed at replay).
- [ADR-017](ADR-017-local-agreement-configuration-as-a-profile.md) — D2 close-then-insert window math; D5 of this ADR cross-references end-exclusive `effective_to` semantics where applicable.
- [ADR-018](ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md) — D7 row-version + If-Match exemplar; D2.2 deferral; this ADR fulfills the deferred propagation.
- [src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs](../../../src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs) — pilot row-version implementation; `OptimisticConcurrencyException` defined here, REUSED across S25 surfaces.
- [src/Backend/StatsTid.Backend.Api/Endpoints/Helpers/EtagHeaderHelper.cs](../../../src/Backend/StatsTid.Backend.Api/Endpoints/Helpers/EtagHeaderHelper.cs) — shared admin-strict If-Match parser, extracted in TASK-2502.
- [SPRINT-25.md](../../sprints/SPRINT-25.md) — sprint log; refinement Q1-Q4 + plan-review cycles 1-3.
- RFC 6585 §4 (Required) — 428 Precondition Required.
- RFC 7232 — Conditional Requests; If-Match / ETag wire format.
