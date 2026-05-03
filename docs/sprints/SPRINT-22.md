# Sprint 22 — Concurrency, Atomicity, Effective-Date Hygiene (Phase 4 Hardening Sprint 1)

| Field | Value |
|-------|-------|
| **Sprint** | 22 |
| **Status** | analysis-phase opening (Step 0a complete; Step 0b + ADR-018 + task decomposition pending) |
| **Start Date** | 2026-05-03 |
| **End Date** | TBD |
| **Orchestrator Approved** | analysis-phase yet to begin |
| **Build Verified** | n/a (no implementation yet) |
| **Test Verified** | n/a (no implementation yet) |

## Entropy Scan Findings

_Sprint 22 Step 0a, 2026-05-03._

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | Only `ADR-016-temporal-period-handling.md` references the S20-retired `OkVersionBoundary` + `RecalculateWithVersionSplitAsync` — that is historical retirement context, not a stale path. No KB entry references a moved or deleted production file. 17 ADR + 6 PAT + 4 DEP + 1 RES + 1 FAIL = 29 entries (matches MEMORY). |
| Pattern compliance spot-check | CLEAN | (a) PAT-005: 0 `using StatsTid.RuleEngine` from `src/Backend/`, `src/Integrations/`, `src/Infrastructure/` (only RuleEngine project itself). (b) FAIL-001: 0 `FindFirst("scopes")`. (c) Hardcoded service URLs: 0 outside `ServiceUrls:*` config-fallback defaults (all matches are `?? "http://service:8080"` after `configuration["ServiceUrls:..."]`). (d) `RequireAuthorization` coverage: 93 endpoints, 88 calls — 5-endpoint gap matches the expected unauthenticated set (5 `/health` endpoints + `/api/auth/login`, with login intentionally pre-auth as the JWT-issuing entry-point). |
| Orphan detection | CLEAN | Post-S21 additions all referenced: `LocalAgreementProfile` consumed by 5 sites (model + repo + migrator + resolution + endpoints), `LocalAgreementProfileRepository` consumed by ConfigEndpoints + ConfigResolutionService + PCS hydration + 18 Docker-gated tests, `ProfileAlignmentValidator` consumed by ConfigEndpoints PUT + 1 unit test, `BoundaryCause.LocalProfileActivation` consumed by `OrderedCauses` + hydration test, `ProfileEditor` + `MondayDatePicker` consumed by `ConfigManagement.tsx`. |
| Documentation drift | CLEAN | `MEMORY.md` refreshed at S21 sprint close (test counts current at 618; deferred items list extended with 4 new S21-derived entries; sprint count now 21). `docs/QUALITY.md` updated with S21 column (Infrastructure B → B+, Backend API B- → B). `docs/sprints/INDEX.md` S18-21 status flipped from "planned" to "complete". `ROADMAP.md` Completed Sprints table extends to S21; Phase 4 section gains the third Step-7a sibling bullet. |
| Quality grade review | CLEAN | Grades current as of S21 sprint close (2026-05-03). No domain quality changes since. |

No DRIFT or DEBT findings. Analysis-phase opens.

## Sprint Goal

Close the three S21-derived correctness carry-forwards as a coordinated trio:

1. **Transactional outbox** for event-store + state-change atomicity (D6 sibling).
2. **ETag/If-Match concurrency-token propagation** across 4 admin-write surfaces beyond `local_agreement_profiles` (D2.2 sibling).
3. **Row-version column + end-exclusive `effective_to` semantics** on `local_agreement_profiles` so in-place edits compose with optimistic concurrency and same-day saves don't produce invalid history windows (Step-7a-D2 sibling).

All three concerns share the same architectural family — they all touch the boundary between state-change writes and event-store appends, and they all rest on what "the current row" means for an optimistic-concurrency token. Sized as **one Phase-4 sprint** so the row-version pattern (Step-7a-D2) is finalized on `local_agreement_profiles` first, then propagated to the four other admin-write surfaces (D2.2) — and the outbox redesign (D6) lands underneath both, removing the post-commit-append residual partial-failure window from the canonical event path.

**This sprint begins with architectural analysis.** No implementation tasks listed yet. The first activity is to produce ADR-018 (or amendments to ADR-001 + ADR-017) and a task decomposition; implementation tasks are drafted only after that analysis is Orchestrator-approved.

## Pre-Sprint Anchoring

- **S21 carry-forwards driving this sprint** (recorded 2026-05-03 at S21 close, see `MEMORY.md` Deferred Items + ADR-017 "For Phase 4 ROADMAP" + ROADMAP.md Phase 4 section):
  - **D2.2** — ETag/If-Match optimistic concurrency pattern propagation across `agreement_configs` (DRAFT edits), `position_overrides`, `wage_type_mappings`, `entitlement_configs`. (Pre-S21 `local_configurations` is being deprecated, so it is NOT a propagation target.)
  - **D6** — Transactional outbox for event-store + state-change atomicity. S21's `ConfigEndpoints` PUT appends `LocalAgreementProfileChanged` post-commit because the in-tx event-append under `RepeatableRead` reads `MAX(stream_version)` from a stale snapshot (concurrent saves → unique-violation on `(stream_id, stream_version)`). Residual partial-failure risk: process crash between profile-tx commit and event-store append leaves audit + profile committed with no event.
  - **Step-7a-D2** — Profile in-place edit + close-then-insert window math. Same-day re-save AND backdate before predecessor's start both produce predecessor `effective_to <= effective_from` (invalid history window). Step-7a cycles 3-9 explored a temporal guard + UPDATE-in-place path; both reverted because each fix produced cascading regressions on profile_id-as-ETag.

- **ADR-018** is the projected ADR number (ADR-017 is taken by S21).

- **Sprint-start commit**: `d8cbd76` (S21 sprint close).

## Problem Statement

### What exists today

- **Event-store contract** (`src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs`): `IEventStore.AppendAsync(streamId, @event, ct)` opens its own connection + transaction. Reads `MAX(stream_version) FROM event_streams WHERE stream_id = @streamId` for the next version, then `INSERT` into `events` + `event_streams`. Self-contained; safe under sequential calls.
- **State-change endpoints** (12+ surfaces across Backend.Api): each follows a pattern of (a) open repo tx, (b) write state-change rows, (c) `tx.CommitAsync`, (d) call `eventStore.AppendAsync(streamId, @event, ct)` post-commit. The post-commit shape exists because S21 cycle-2 review showed in-tx event append under `RepeatableRead` collides on `MAX(stream_version)` with concurrent saves. Residual: process-crash window between commit and append leaves the projection table updated with no corresponding event in the event store. Today's audit columns + projection rebuilders mostly mask this for normal operation, but the discontinuity is real.
- **Admin-write concurrency control today**:
  - `local_agreement_profiles` (S21): partial-unique-index `WHERE effective_to IS NULL` + `SELECT FOR UPDATE` + ETag-via-profile_id + `If-Match`/`If-None-Match: *` → 412 on mismatch (ADR-017 D2.1).
  - `agreement_configs` (S12): DRAFT/ACTIVE/ARCHIVED lifecycle. UPDATE on DRAFT-status rows has no concurrency token; two GlobalAdmins editing the same DRAFT both succeed, second silently overwrites first.
  - `position_overrides` (S11/S14): UPDATE on the row, no token; same lost-update race.
  - `wage_type_mappings` (S14): UPDATE on the row, no token; same race.
  - `entitlement_configs` (S15): UPDATE on the row, no token; same race.
- **`local_agreement_profiles` close-then-insert window math** (S21 ADR-017 D2): predecessor closes at `newProfile.EffectiveFrom - 1`. When `newProfile.EffectiveFrom <= predecessor.EffectiveFrom`, predecessor's `effective_to <= effective_from`. Three call sites trigger this:
  1. **In-place edit** — admin loads profile, changes a value, saves without changing effective_from. Most common.
  2. **Same-day creation** — admin saves a profile with effective_from = today, predecessor was created today.
  3. **Backdate before predecessor** — admin error; rare.

### Failure modes today

1. **Lost update on admin write** (D2.2 surfaces): two LocalAdmins/GlobalAdmins editing the same `agreement_configs` DRAFT or `position_overrides` row both succeed; second overwrites first with no warning.
2. **Orphaned state-change without event** (D6): state-change tx commits, process crashes before event-store append. The projection table reflects the change but the event store does not — replay-from-events would diverge from the read model. Currently masked because (a) most projection rebuilders read from projection tables not events, and (b) the crash window is narrow.
3. **Invalid history window** (Step-7a-D2): in-place edits via `ConfigEndpoints` PUT produce `effective_to < effective_from` rows in `local_agreement_profiles`. Currently masked because (a) `WHERE effective_to IS NULL` filters them out of "current open" reads, and (b) `GetActivationsInPeriodAsync` filters `effective_from > periodStart` so they don't surface as boundaries. But the rows persist; any reader that doesn't apply the same filters sees nonsense.

### What "right" looks like (proposed direction, subject to ADR-018)

- **Outbox**: a new `outbox_events` table inside the **same** transaction as state-change writes. A separate publisher process drains the outbox to the canonical event store with at-least-once semantics. State-change tx commits → outbox row visible → publisher picks up → event store gets the event. No partial-failure window. `IEventStore.AppendAsync` either (a) becomes the publisher's tool only, with state-change sites using a new `IOutboxEnqueue` interface, or (b) gains an `AppendInTransaction(conn, tx, …)` overload that writes to outbox.
- **ETag/If-Match propagation**: each admin-write surface gains an `ETag` header on GET responses, an `If-Match` precondition on PUT. Repository UPDATEs include a row-version check (`UPDATE … WHERE row_id = @id AND row_version = @expectedVersion` returning 0 → 412). Common shape extracted into a shared helper or base class.
- **Row-version + end-exclusive on `local_agreement_profiles`**: schema gains a `version` column (BIGINT, auto-increments on UPDATE via trigger or app-side bump). ETag becomes `"{profile_id}:{version}"` (or just `{version}` keyed on profile_id from the URL). Save flow: in-place UPDATE if dates match (bumps version), close-then-insert if dates don't match (creates new row with new profile_id; predecessor's `effective_to = newProfile.EffectiveFrom` exclusive). Same-day re-save → UPDATE in place, no negative range. Backdate before predecessor → still rejected (now via the predecessor-effective-from comparison, but the comparison is `<` not `<=` because end-exclusive eliminates the off-by-one).

## Context and Existing Partial Solutions

Work the new design must build on or reconcile with:

- **`src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs`** — current event-store implementation; will gain or have peer the outbox path.
- **`src/SharedKernel/StatsTid.SharedKernel/Events/IEventStore.cs`** — current interface; outbox shape may extend it or introduce a sibling.
- **`src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs`** — pilot implementation of ETag/If-Match (S21 ADR-017 D2.1); the row-version + end-exclusive change lands here first.
- **`src/Infrastructure/StatsTid.Infrastructure/AgreementConfigRepository.cs`** — D2.2 propagation target. Today's UPDATE has no concurrency token.
- **`src/Infrastructure/StatsTid.Infrastructure/PositionOverrideRepository.cs`** — D2.2 propagation target.
- **`src/Infrastructure/StatsTid.Infrastructure/WageTypeMappingRepository.cs`** — D2.2 propagation target.
- **`src/Infrastructure/StatsTid.Infrastructure/EntitlementConfigRepository.cs`** — D2.2 propagation target.
- **`src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs`** — pilot PUT/GET handlers with ETag/If-Match.
- **`src/Backend/StatsTid.Backend.Api/Endpoints/AgreementConfigEndpoints.cs`** — D2.2 propagation target (PUT, possibly POST for status transitions).
- **`src/Backend/StatsTid.Backend.Api/Endpoints/PositionOverrideEndpoints.cs`** — D2.2 propagation target.
- **`src/Backend/StatsTid.Backend.Api/Endpoints/WageTypeMappingEndpoints.cs`** — D2.2 propagation target.
- **`src/Backend/StatsTid.Backend.Api/Endpoints/`** (entitlement endpoints, currently in BalanceEndpoints + Admin) — D2.2 propagation target.
- **ADR-001 (Event Sourcing PostgreSQL Npgsql)** — outbox is an extension; keep the architectural invariant.
- **ADR-004 (Outbox Pattern Guaranteed Delivery)** — name conflict! ADR-004 is titled "Outbox Pattern" but it's about the existing per-event delivery from event-store to integration consumers, NOT the transactional outbox we need for state-change-to-event-store atomicity. ADR-018 must clarify the distinction or amend ADR-004.
- **ADR-017 (Local Agreement Configuration as a Profile)** — D2.1 ETag pattern is the reference implementation; D2.2 propagation is part of this sprint's goal; D2 close-then-insert window math is amended to end-exclusive here.

## Legal & Correctness Constraints (must not regress)

1. **P1 — Architectural integrity.** Outbox is additive, doesn't change the rule engine or service topology. ETag propagation is additive. Row-version + end-exclusive semantics must keep all existing reads (ConfigResolutionService, BoundarySources hydration, history queries) working.
2. **P3 — Auditability.** Every state-change still produces exactly one event in the canonical store (at-least-once with idempotency on the consumer side). Event count must not regress.
3. **P5 — Integration isolation.** Outbox changes the **how** of event publication, not the **what**. Downstream consumers (payroll, external integrations) see the same event shapes.
4. **P7 — Security.** Concurrency tokens don't bypass `OrgScopeValidator` or `RequireAuthorization`. ETag is a precondition check, not an auth check.
5. **Backwards compatibility.** Existing pre-S22 events stay readable. Existing pre-S22 audit rows stay queryable. Existing pre-S22 `local_agreement_profiles` rows (the S21-shaped ones) get a default version (e.g., `1`) on schema migration; subsequent in-place edits bump the version.

## Open Architectural Questions (to answer at sprint start)

These must be resolved — and documented in ADR-018 (or amendments to ADR-001 + ADR-004 + ADR-017) — **before** a task decomposition is drafted.

1. **Outbox shape — same DB or separate?** PostgreSQL `outbox_events` table inside the same database as the projection tables is the simple option (single transaction). A separate event-store DB would require 2-phase commit or sagas; over-engineered for a state in which the canonical event store is already PostgreSQL.

2. **Outbox publisher — in-process or separate?** Background hosted service inside Backend.Api (simpler ops, single deployment) vs. a dedicated `StatsTid.OutboxPublisher` worker (cleaner separation, can scale independently). For pre-production pre-launch, in-process is the lighter footprint.

3. **`IEventStore` evolution.** Three options:
   - (a) State-change sites switch to a new `IOutboxEnqueue.EnqueueAsync(streamId, @event, conn, tx, ct)` interface; `IEventStore.AppendAsync` is no longer called by state-change sites (only by the outbox publisher).
   - (b) `IEventStore.AppendAsync` gains an in-tx overload `AppendAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string streamId, IDomainEvent @event, ct)` that writes to outbox; the existing self-contained overload becomes outbox-publisher-only.
   - (c) `IEventStore` is renamed to `IEventLog` (the projection of "what really happened"); a new `IEventEmitter` interface owns the "I want to emit an event in this transaction" verb; outbox is the implementation.

4. **Outbox row layout.** Minimum viable: `outbox_id BIGSERIAL`, `stream_id`, `event_type`, `event_payload JSONB`, `created_at`, `published_at NULLABLE`, `attempts INT`, `last_error TEXT NULLABLE`. The `published_at` doubles as "consumed" marker. Need an index on `WHERE published_at IS NULL` for the publisher's polling query.

5. **At-least-once → idempotency on the consumer side.** The publisher MAY double-publish on crash-during-publish. The event store's `(stream_id, stream_version)` UNIQUE constraint provides natural deduplication, but the version assignment must be deterministic. Three options:
   - (a) Outbox row carries `stream_version` computed at enqueue time. Publisher just inserts at that version. Risk: concurrent enqueues for the same stream from different transactions race on `MAX(stream_version)` (the same problem S21 cycle-2 hit).
   - (b) Outbox row does NOT carry version; publisher computes `stream_version = MAX(stream_version) + 1` at publish time, with `FOR UPDATE` on the stream row. Simpler enqueue path, sequential publishing per stream.
   - (c) Per-stream sequencer table (`event_streams.next_version` updated by publisher in tx). Same as (b) effectively.

6. **Publisher ordering guarantees.** Per-stream FIFO (events on the same stream publish in enqueue order) is essential. Cross-stream ordering does NOT need a guarantee — events on different streams are independent. Publisher implementation: poll oldest unpublished outbox rows, group by `stream_id`, publish in `outbox_id` order.

7. **Row-version on `local_agreement_profiles` — column type and bump mechanism.**
   - Type: `BIGINT NOT NULL DEFAULT 1`. (XMIN-based optimistic concurrency is a PostgreSQL trick that works without a column but is opaque to the application.)
   - Bump: trigger (`BEFORE UPDATE`) auto-incrementing → invisible to repository code. OR app-side `UPDATE … SET version = version + 1`. Trigger is more bullet-proof; app-side keeps the repository self-documenting.

8. **End-exclusive `effective_to` semantics.** Today's S21 schema is end-inclusive (a row with `effective_from=2026-01-01, effective_to=2026-03-31` is active for January through March). End-exclusive flips to "active for `[effective_from, effective_to)`" — predecessor closes at `effective_to = newProfile.EffectiveFrom` (no `-1`). Same-day re-saves no longer produce negative ranges. **Migration question**: do existing S21-shaped rows convert (`effective_to + 1` in the schema migration) or do new rows use the new convention while old rows stay end-inclusive (mixed)? Mixed is footgun-prone; convert in migration.

9. **In-place edit detection in repo.** With end-exclusive `effective_to`, the repository can detect "same-day save" by `newProfile.EffectiveFrom == predecessor.EffectiveFrom` and route to UPDATE. Should the routing be **automatic** (repo decides) or **explicit** (caller passes `EditMode.InPlace` / `EditMode.Supersede`)? Automatic is friendlier; explicit is safer for code review. ADR-017's existing "PUT == one save semantic" suggests automatic.

10. **ETag propagation pattern — shared helper or per-repo replication?** S21's `LocalAgreementProfileRepository` has the ETag/If-Match logic inline. The 4 D2.2 surfaces (`agreement_configs`, `position_overrides`, `wage_type_mappings`, `entitlement_configs`) could each replicate the pattern, OR a shared `OptimisticConcurrencyHelper` could centralize it. Centralization risk: each surface has slightly different lifecycle states (DRAFT/ACTIVE/ARCHIVED on agreement_configs; simpler on the others); the helper has to thread that.

11. **DRAFT vs ACTIVE concurrency on `agreement_configs`.** S12's lifecycle is DRAFT/ACTIVE/ARCHIVED. Edits to DRAFT mutate the same row; ACTIVE → ARCHIVED clones. ETag applies cleanly to DRAFT edits. Does the publish transition (DRAFT → ACTIVE) need its own If-Match? Probably yes — same lost-update race.

12. **Test strategy — committed minimum matrix.** Following ADR-016 D11 + ADR-017 D11 format. Categories pre-committed as IN; scenario depth committed per category:
    - **Outbox enqueue + publish** (regression, Docker-gated): minimum 4 scenarios — happy path enqueue+publish, publisher restart resumes from oldest unpublished, duplicate-publish protected by UNIQUE constraint, per-stream FIFO ordering. Floor: ≥ 4 tests in `tests/StatsTid.Tests.Regression/Outbox/`.
    - **ETag/If-Match per surface** (regression, Docker-gated): minimum 2 scenarios per surface × 4 surfaces = 8 — concurrent UPDATE detected via row-version mismatch → 412; UPDATE without If-Match → 412 (or 428 missing precondition). Floor: ≥ 8 tests across `tests/StatsTid.Tests.Regression/Concurrency/`.
    - **Row-version on profile** (unit + regression): unit tests for the bump on UPDATE; Docker-gated test for the in-place edit path producing valid history (no negative range). Floor: ≥ 3 tests.
    - **End-exclusive migration**: regression test that pre-S22 rows (end-inclusive) convert correctly to end-exclusive without changing the displayed active period. Floor: ≥ 2 tests.
    - **Floor: 17 new tests.** Cells beyond floor add 1:1 against ADR-018's resolved scenarios.

## Scope Boundary

### In scope
- ADR-018 (or amendments to ADR-001 + ADR-004 + ADR-017) covering the trio.
- Outbox table schema + publisher service + `IEventStore` / `IEventEmitter` interface evolution.
- Row-version column on `local_agreement_profiles` + end-exclusive `effective_to` migration + repository UPDATE-in-place path.
- ETag/If-Match propagation across `agreement_configs` (DRAFT edits + status transitions), `position_overrides`, `wage_type_mappings`, `entitlement_configs`. Includes endpoint changes + repository row-version checks.
- Backfill row-version columns on existing rows of all 5 surfaces (default `1`).
- Migration of existing `local_agreement_profiles` rows to end-exclusive `effective_to`.
- Regression coverage per Q12's committed minimum matrix.

### Out of scope
- Changing the event store's *contents* — payloads stay the same, just the path of arrival changes.
- Versioned-history for non-dated boundary sources (Phase 4 X-1/X-2/X-3 sub-sprints — separate Phase-4 work).
- UI/UX polish on optimistic-concurrency error rendering (Phase 5 owns polish).
- Touching `local_configurations` legacy table (deprecated post-S21; admins write via profiles).
- Touching JWT, RBAC, or `OrgScopeValidator` — those layers stay unchanged.

## Planning Entrypoint

No implementation tasks defined yet. Sprint begins with the following **analysis-phase deliverables**:

1. **Architectural ADR** (ADR-018 — title TBD: "Transactional Outbox + Optimistic Concurrency for Admin-Write Surfaces"). Answers Q1-Q12.
2. **Migration plan** — explicit handling of existing rows on all 5 surfaces (row-version backfill + end-exclusive conversion on profiles).
3. **Task decomposition** — ADR translated into `TASK-22NN` entries with domain agents, file scopes, and validation criteria.
4. **Entropy scan (Step 0a)** — completed 2026-05-03; findings recorded above.
5. **Plan review (Step 0b)** — pending.

Only after items 1-3 are Orchestrator-approved does Step 2 (Delegate) begin.

## Plan Review (Step 0b)

_Pending. Trigger: MANDATORY (P1 architectural integrity + P3 auditability + P5 integration isolation + cross-domain Data Model + Backend API + Test & QA + introduces new abstractions: outbox publisher, ETag pattern propagation, row-version column)._

## References

- [CLAUDE.md](../../CLAUDE.md) — priority order
- [ROADMAP.md](../../ROADMAP.md) — Phase 4 placement (3-sibling commitment)
- [SPRINT-21.md](SPRINT-21.md) — origin of the three carry-forwards
- [docs/knowledge-base/decisions/ADR-001-event-sourcing-postgresql-npgsql.md](../knowledge-base/decisions/ADR-001-event-sourcing-postgresql-npgsql.md) — current event store design
- [docs/knowledge-base/decisions/ADR-004-outbox-pattern-guaranteed-delivery.md](../knowledge-base/decisions/ADR-004-outbox-pattern-guaranteed-delivery.md) — existing "outbox" usage (downstream delivery, NOT state-change-to-event-store atomicity); ADR-018 must clarify the distinction
- [docs/knowledge-base/decisions/ADR-017-local-agreement-configuration-as-a-profile.md](../knowledge-base/decisions/ADR-017-local-agreement-configuration-as-a-profile.md) — D2.1 ETag pattern reference + D2.2/D6/Step-7a-D2 carry-forward triggers
- [src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs](../../src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs) — current event-store implementation
- [src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs](../../src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs) — pilot ETag/If-Match implementation
- [src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs](../../src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs) — pilot PUT handler with concurrency precondition
