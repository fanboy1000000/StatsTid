# Sprint 22 — Transactional Outbox + Profile Row-Version (Phase 4 Hardening Foundation)

| Field | Value |
|-------|-------|
| **Sprint** | 22 |
| **Status** | analysis-phase (Step 0a complete; Step 0b cycle 1 complete; ADR-018 + task decomposition pending) |
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
| Pattern compliance spot-check | CLEAN | (a) PAT-005: 0 `using StatsTid.RuleEngine` from `src/Backend/`, `src/Integrations/`, `src/Infrastructure/` (only RuleEngine project itself). (b) FAIL-001: 0 `FindFirst("scopes")`. (c) Hardcoded service URLs: 0 outside `ServiceUrls:*` config-fallback defaults. (d) `RequireAuthorization` coverage: 93 endpoints, 88 calls — 5-endpoint gap. The 5 unauthenticated endpoints are 4 `/health` endpoints (Backend.Api, Payroll, External, Orchestrator — RuleEngine's `/health` IS authenticated) plus `/api/auth/login`. Confirmed by direct read; the original framing said 5 health endpoints which was wrong. |
| Orphan detection | CLEAN | Post-S21 additions all referenced. |
| Documentation drift | CLEAN | MEMORY.md, QUALITY.md, INDEX.md, ROADMAP.md all refreshed at S21 sprint close (2026-05-03). |
| Quality grade review | CLEAN | Grades current as of S21 sprint close. |

No DRIFT or DEBT findings. Analysis-phase opens.

## Sprint Goal

Land the **foundation** of the Phase-4 hardening trio originally scoped together: the transactional outbox (D6) and the row-version + end-exclusive `effective_to` redesign of `local_agreement_profiles` (Step-7a-D2). The third concern (D2.2 ETag/If-Match propagation across the four other admin-write surfaces) ships as a separate sprint (S23) once this sprint's row-version-as-ETag pattern is real production code instead of an ADR sketch.

The split was made at Step 0b plan review (2026-05-03, see "Plan Review" section below): the Reviewer surfaced that the trio's apparent unity hides three real seams — Q3/Q5 coupling around `IEventStore` evolution, `agreement_configs` having three distinct race-conditions instead of one, and two distinct ETag shapes (`profile_id`-as-ETag vs `version`-only) needing reconciliation. Each seam alone is non-trivial; bundling them risks the Step-7a death-spiral pattern S21 documented (cycle-cap discipline feedback, 2026-05-03).

S22's deliverable: outbox + profile redesign + a published exemplar (ETag pattern proven in production code on `local_agreement_profiles`) that S23 propagates. ADR-018 covers the outbox + row-version architecture; D2.2 decisions defer to ADR-019 in S23.

**This sprint begins with architectural analysis.** No implementation tasks listed yet. ADR-018 + task decomposition before any code.

## Pre-Sprint Anchoring

- **S21 carry-forwards driving this sprint** (recorded 2026-05-03 at S21 close, see `MEMORY.md` Deferred Items + ADR-017 "For Phase 4 ROADMAP" + ROADMAP.md Phase 4 section):
  - **D6** — Transactional outbox for event-store + state-change atomicity. S21's `ConfigEndpoints` PUT appends `LocalAgreementProfileChanged` post-commit because the in-tx event-append under `RepeatableRead` reads `MAX(stream_version)` from a stale snapshot (concurrent saves → unique-violation on `(stream_id, stream_version)`). Residual partial-failure risk: process crash between profile-tx commit and event-store append leaves audit + profile committed with no event.
  - **Step-7a-D2** — Profile in-place edit + close-then-insert window math. Same-day re-save AND backdate before predecessor's start both produce predecessor `effective_to <= effective_from` (invalid history window). Step-7a cycles 3-9 explored a temporal guard + UPDATE-in-place path; both reverted because each fix produced cascading regressions on profile_id-as-ETag.

- **D2.2** (ETag propagation across `agreement_configs`, `position_overrides`, `wage_type_mappings`, `entitlement_configs`) — explicitly deferred to S23. SPRINT-23.md placeholder drafted alongside this commit.

- **ADR-018** is the projected ADR number (ADR-017 is taken by S21).

- **Sprint-start commit**: `d8cbd76` (S21 sprint close).

## Problem Statement

### What exists today

- **Event-store contract** (`src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs`): `IEventStore.AppendAsync(streamId, @event, ct)` opens its own connection + transaction. Reads `MAX(stream_version) FROM event_streams WHERE stream_id = @streamId` for the next version, then `INSERT` into `events` + `event_streams`. Self-contained; safe under sequential calls.
- **State-change endpoints** (12+ surfaces across Backend.Api, plus state-change writes in Payroll and External integrations): each follows a pattern of (a) open repo tx, (b) write state-change rows, (c) `tx.CommitAsync`, (d) call `eventStore.AppendAsync` post-commit. The post-commit shape exists because S21 cycle-2 review showed in-tx event append under `RepeatableRead` collides on `MAX(stream_version)` with concurrent saves.
- **Residual partial-failure window**: process-crash between commit and append leaves the projection table updated with no corresponding event in the event store. Today's audit columns + projection rebuilders mostly mask this for normal operation, but the discontinuity is real.
- **`local_agreement_profiles` close-then-insert window math** (S21 ADR-017 D2): predecessor closes at `newProfile.EffectiveFrom - 1`. When `newProfile.EffectiveFrom <= predecessor.EffectiveFrom`, predecessor's `effective_to <= effective_from`. Three call sites trigger this:
  1. **In-place edit** — admin loads profile, changes a value, saves without changing effective_from. Most common.
  2. **Same-day creation** — admin saves a profile with effective_from = today, predecessor was created today.
  3. **Backdate before predecessor** — admin error; rare.
- **ETag-via-`profile_id`** (S21 ADR-017 D2.1): the pilot ETag mechanism uses the profile's UUID as its concurrency token. This works because S21's close-then-insert path always rotates `profile_id` (new row → new UUID). It DOES NOT work for in-place UPDATE because the UUID stays stable while the row's contents change.

### Failure modes today

1. **Orphaned state-change without event** (D6): state-change tx commits, process crashes before event-store append. The projection table reflects the change but the event store does not — replay-from-events would diverge from the read model. Currently masked because (a) most projection rebuilders read from projection tables not events, and (b) the crash window is narrow.
2. **Invalid history window** (Step-7a-D2): in-place edits via `ConfigEndpoints` PUT produce `effective_to < effective_from` rows in `local_agreement_profiles`. Currently masked because (a) `WHERE effective_to IS NULL` filters them out of "current open" reads, and (b) `GetActivationsInPeriodAsync` filters `effective_from > periodStart` so they don't surface as boundaries. But the rows persist; any reader that doesn't apply the same filters sees nonsense.
3. **Cycle-9-confirmed coupling**: ETag-via-profile_id + close-then-insert + same-day saves are mutually unsatisfiable without (i) a separate row-version token AND (ii) clean end-exclusive `effective_to` semantics. S21 Step 7a explored half-fixes (cycles 3-9) and demonstrated that EITHER half alone leaves a known-bad case.

### What "right" looks like (subject to ADR-018)

- **Outbox**: a new `outbox_events` table inside the **same** transaction as state-change writes. A separate publisher process drains the outbox to the canonical event store with at-least-once semantics. State-change tx commits → outbox row visible → publisher picks up → event store gets the event. No partial-failure window. State-change sites switch to a new in-tx-aware enqueue verb; the existing `IEventStore.AppendAsync` becomes the publisher's tool only.
- **Row-version on `local_agreement_profiles`**: schema gains a `version` column (BIGINT). Column auto-increments on UPDATE via app-side bump (visible to repository code). ETag becomes `"{version}"` keyed implicitly on the URL's `(orgId, agreementCode, okVersion)` triple. ADR-017 D2.1's `If-Match: <profile_id>` shape becomes `If-Match: <version>` — semantically equivalent to "the row at version V is what I read."
- **End-exclusive `effective_to`**: schema invariant flips to "active for `[effective_from, effective_to)`". Predecessor closes at `effective_to = newProfile.EffectiveFrom` (no `-1`). Same-day re-save → repository routes to UPDATE-in-place (bumps version, no new row, no negative range). Backdate before predecessor → still rejected by an explicit guard (now `<` not `<=` because end-exclusive eliminates the off-by-one). Migration converts existing rows.
- **Replay-determinism**: pre-S22 `SegmentManifest`s capture profile-activation **boundaries** (dates), not profile snapshots — local-config is NOT in ADR-016 D5b's snapshot-at-calculation set. End-inclusive→end-exclusive shifts a predecessor's `effective_to` value by +1 day in the schema, but the boundary detection's `effective_from` is unchanged, so manifest replay should be invariant. ADR-018 includes an explicit replay-determinism proof per ADR-017 D10's pattern.

## Context and Existing Partial Solutions

- **`src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs`** — current event-store implementation; the publisher's tool post-S22.
- **`src/SharedKernel/StatsTid.SharedKernel/Events/IEventStore.cs`** — current interface. ADR-018 decides whether state-change sites use this interface or a new `IEventEmitter`.
- **`src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs`** — pilot implementation of ETag/If-Match (S21 ADR-017 D2.1); the row-version + end-exclusive change lands here.
- **`src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs`** — pilot PUT/GET handlers with ETag/If-Match.
- **State-change-emitting endpoints** that need to switch from post-commit `eventStore.AppendAsync` to in-tx outbox enqueue: `ConfigEndpoints`, `AdminEndpoints`, `AgreementConfigEndpoints`, `PositionOverrideEndpoints`, `WageTypeMappingEndpoints`, `EntitlementEndpoints` (in BalanceEndpoints + Admin), `ApprovalEndpoints`, `OvertimeEndpoints`, `ComplianceEndpoints`, `SkemaEndpoints`, `TimerEndpoints`, `TimeEndpoints`, plus state-change sites in `Payroll` and `External` integrations. **Note**: each call site is already in a transactional context (`await using var tx = ...`) — the migration is mostly mechanical "swap one line" once the new in-tx interface lands.
- **`SegmentManifest` projection** (`segment_manifests` table) and ADR-016 D5b — local-config is NOT in the snapshot-at-calculation set, so manifest payloads don't carry profile snapshots. End-exclusive migration affects the row data but not the manifest data. ADR-018 confirms with a replay-determinism proof.
- **ADR-001 (Event Sourcing PostgreSQL Npgsql)** — outbox is an extension; keep the architectural invariant.
- **ADR-004 (Outbox Pattern Guaranteed Delivery)** — _approved S1 but never implemented._ ADR-004 commits to an outbox for "guaranteed delivery to integrations" but the outbox table doesn't exist in `init.sql`; the per-event delivery to integrations actually happens via direct HTTP calls in S5+ payroll/external integration code. ADR-018 **supersedes** ADR-004, explicitly fulfilling the original commitment AND adding state-change-to-event-store atomicity which ADR-004 did not address.
- **ADR-017** — D2.1 ETag pattern is the reference implementation; D2 close-then-insert window math is amended to end-exclusive here (ADR-018 amends ADR-017 D2 + D2.1).

## Legal & Correctness Constraints (must not regress)

1. **P1 — Architectural integrity.** Outbox is additive, doesn't change the rule engine or service topology. Row-version + end-exclusive semantics must keep all existing reads (ConfigResolutionService, BoundarySources hydration, history queries) working.
2. **P3 — Auditability.** Every state-change still produces exactly one event in the canonical store (at-least-once with idempotency on the consumer side via the `(stream_id, stream_version)` UNIQUE constraint). Event count must not regress; cardinality must not change.
3. **P5 — Integration isolation.** Outbox changes the **how** of event publication, not the **what**. Downstream consumers (payroll, external integrations) see the same event shapes.
4. **P7 — Security.** Concurrency tokens don't bypass `OrgScopeValidator` or `RequireAuthorization`. ETag is a precondition check, not an auth check.
5. **Backwards compatibility.** Existing pre-S22 events stay readable. Existing pre-S22 audit rows stay queryable. Existing pre-S22 `local_agreement_profiles` rows (the S21-shaped ones) get `version = 1` on schema migration; their `effective_to` values get `+1` to convert to end-exclusive (or stay NULL if open). Subsequent in-place edits bump version.
6. **Replay determinism.** Pre-S22 `SegmentManifest`s must replay identically against post-S22 profile rows. The end-inclusive→end-exclusive conversion must be a pure relabeling: a row that was active for January through March (end-inclusive `effective_to=2026-03-31`) is now `[2026-01-01, 2026-04-01)` (end-exclusive `effective_to=2026-04-01`). Boundary detection reads `effective_from` (unchanged) → manifests reproduce.

## Open Architectural Questions (to answer in ADR-018)

These must be resolved **before** task decomposition. Step 0b plan review (cycle 1, 2026-05-03) closed Q3/Q5 to single-answer pairs and dropped option Q5(a) as the "regenerates S21 cycle-2 conflict" antipattern.

1. **Outbox shape — same DB or separate?** Single PostgreSQL DB (the existing `statstid` DB) is the only viable option — separate event-store DB requires 2-PC across stores, over-engineered. **Pre-committed: single DB, `outbox_events` table alongside projection tables.**

2. **Outbox publisher — in-process or separate?** Three options:
   - (a) **In-process hosted service inside Backend.Api.** Simplest deployment but couples publisher uptime to Backend.Api uptime AND assumes Backend.Api is the only service writing state-change events. State-change writes ALSO happen in `Payroll` (`PeriodCalculationCompleted`, `PayrollExportGenerated`, `IntegrationDeliveryTracked`) and `External` integrations. Per-service writers → per-service outbox? Or one writer cross-service?
   - (b) **Per-service in-process publisher.** Each service owns its own outbox table; the canonical event store merges by global ordering. Simpler isolation but multiple polling loops.
   - (c) **Dedicated `StatsTid.OutboxPublisher` worker service.** Clean separation, can scale independently. New 9th docker service. Heavier ops footprint.
   - **Plan-review note (cycle 1, W4):** options must explicitly enumerate the cross-service write topology before commitment.

3. **`IEventStore` evolution.** Single-answer (forced by Q5):
   - **Pre-committed: option (b)** — `IEventStore.AppendAsync` gains an in-tx overload `AppendAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string streamId, IDomainEvent @event, ct)` that writes to the outbox. The existing self-contained overload becomes outbox-publisher-only. State-change sites call the in-tx overload; the publisher calls the self-contained overload (or a private `AppendInternalAsync`).
   - Rationale: Q5's resolution forces publish-time version assignment, which means state-change sites cannot pre-compute `stream_version` at enqueue time. Option (a) (`IOutboxEnqueue`) and (c) (`IEventEmitter`) are renames of (b) without changing the MVCC story; (b) keeps the API minimal.
   - Plan-review note (cycle 1, B2): Q3-Q5 coupling stated in ADR-018; alternative options listed only for forward-readability.

4. **Outbox row layout.** Minimum viable:
   ```
   outbox_id        BIGSERIAL    PRIMARY KEY
   stream_id        TEXT         NOT NULL
   event_type       TEXT         NOT NULL
   event_payload    JSONB        NOT NULL
   created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
   published_at     TIMESTAMPTZ  NULL
   stream_version   BIGINT       NULL    -- assigned by publisher; populated post-publish for join debugging
   attempts         INT          NOT NULL DEFAULT 0
   last_error       TEXT         NULL
   ```
   Index: `WHERE published_at IS NULL` for the publisher's polling query. The `stream_version` column is debug-only (populated after publish for outbox→events joins).

5. **At-least-once → idempotency on the consumer side.** Single-answer:
   - **Pre-committed: option (b)** — outbox row does NOT carry `stream_version` at enqueue time; publisher computes `MAX(stream_version) + 1` at publish time within its own self-contained transaction (with `SELECT FOR UPDATE` on the stream row). Sequential publishing per stream.
   - Option (a) was the cycle-1-cycle-2 antipattern from S21 (in-tx `MAX(stream_version)` reads from stale snapshot under `RepeatableRead`); listed here only for forward-readability.
   - Option (c) is mechanically equivalent to (b); skipped.
   - Plan-review note (cycle 1, B1): option (a) explicitly rejected with rationale; the sprint exists because of option (a)'s failure mode.

6. **Publisher ordering guarantees.** Per-stream FIFO (events on the same stream publish in enqueue order) is essential. Cross-stream ordering does NOT need a guarantee — events on different streams are independent. Publisher implementation: poll oldest unpublished outbox rows, group by `stream_id`, publish in `outbox_id` order per stream.

7. **Row-version on `local_agreement_profiles` — column type and bump mechanism.**
   - Type: `BIGINT NOT NULL DEFAULT 1`.
   - Bump: app-side `UPDATE … SET version = version + 1` rather than DB trigger. Repository-visible, easier to test, single source of truth for the bump.
   - Plan-review note (cycle 1, N1): debug-friendly version surfaces in audit rows + the outbox row's `stream_version` post-publish join.

8. **End-exclusive `effective_to` migration.** Single-answer:
   - **Pre-committed: convert in migration.** `effective_to + 1` for every closed (non-NULL) row. Atomic with the row-version backfill. Open rows (`effective_to IS NULL`) stay NULL.
   - **Replay-determinism proof** (ADR-018 includes this section per ADR-017 D10's pattern):
     - `SegmentManifest` payloads contain boundary dates derived from `LocalProfileActivation` events (`effective_from`).
     - End-exclusive migration changes `effective_to` values; `effective_from` is unchanged.
     - `LocalAgreementProfileRepository.GetActivationsInPeriodAsync` reads `effective_from > periodStart AND effective_from <= periodEnd` plus `effective_to IS NULL OR effective_to >= periodStart` — the `effective_to` filter changes meaning under end-exclusive (`>=` becomes `>` because a row with `effective_to = periodStart` was historically active *through* periodStart, but under end-exclusive ended *before* periodStart).
     - **Migration-time predicate update**: every read site that filters on `effective_to` enumerated in ADR-018 with the predicate update.
     - **Test strategy (Q12)**: regression test that pre-S22 manifests replay to byte-identical output post-S22.
   - Plan-review note (cycle 1, B3): explicit per-read-site predicate audit committed; replay-determinism proof committed.

9. **In-place edit detection in repo.** Single-answer:
   - **Pre-committed: automatic.** Repository detects `newProfile.EffectiveFrom == predecessor.EffectiveFrom` and routes to UPDATE-in-place (bumps version, audit-action `MODIFIED`). Otherwise routes to close-then-insert (predecessor `effective_to = newProfile.EffectiveFrom` end-exclusive; new row `version = 1`; audit-action `SUPERSEDED`).
   - Audit-action enum extension: add `'MODIFIED'` to the `local_agreement_profile_audit.action` CHECK constraint. Schema migration in same DDL block as the row-version + end-exclusive conversion.
   - Plan-review note (cycle 1, W6): audit-action enum extension committed; not deferred.

10. **Read-site predicate update under end-exclusive.** Single-answer:
    - **Pre-committed: enumerate every read site in ADR-018 with the predicate change.** Affected sites (preliminary):
      - `LocalAgreementProfileRepository.GetCurrentOpenAsync` — uses `effective_to IS NULL` only; no change.
      - `LocalAgreementProfileRepository.GetActivationsInPeriodAsync` — `effective_to >= @periodStart` becomes `effective_to > @periodStart` (a row whose end-exclusive `effective_to == periodStart` does NOT overlap the period).
      - `LocalAgreementProfileRepository.GetHistoryAsync` — `effective_to IS NOT NULL` only; no change.
      - `ConfigResolutionService` profile lookup — `GetCurrentOpenAsync`; no change.
      - `BuildPlanForLegacyCallers` D9c hydration in `PeriodCalculationService` — calls `GetActivationsInPeriodAsync`; predicate change propagates.
    - Plan-review note (cycle 1, N4): D9c hydration predicate update audit committed.

11. **ADR-004 supersession.** Single-answer:
    - **Pre-committed: ADR-018 supersedes ADR-004.** ADR-004 was approved S1 but never implemented; the per-event integration delivery actually happens via direct HTTP from `Payroll` and `External` services, not through an outbox. ADR-018 implements the original commitment AND adds state-change-to-event-store atomicity which ADR-004 did not address. ADR-004 marked "superseded by ADR-018" in its header.
    - Plan-review note (cycle 1, W1): supersession is the architecturally honest framing.

12. **Test strategy — committed minimum matrix.** Following ADR-016 D11 + ADR-017 D11 format. Categories pre-committed as IN; scenario depth committed per category:
    - **Outbox enqueue + publish** (regression, Docker-gated): **5 scenarios**
      1. Happy path enqueue+publish: state-change tx commits, outbox row visible, publisher picks up, event-store row appears with assigned `stream_version`.
      2. Publisher restart resumes from oldest unpublished: kill publisher mid-batch, restart, no events lost or duplicated (idempotent on `(stream_id, stream_version)`).
      3. Per-stream FIFO ordering: 3 enqueues on same stream in order A, B, C → event-store rows appear in order A, B, C with consecutive `stream_version`.
      4. Concurrent cross-stream parallelism: enqueues on streams X and Y interleave; publisher publishes them concurrently without ordering interference.
      5. **Rolled-back state-change → no outbox row visible → publisher does not publish** (the inverse-direction guarantee that defines transactional-outbox correctness — Reviewer cycle-1 B4).
    - **Profile row-version + end-exclusive** (regression, Docker-gated): **4 scenarios**
      1. In-place UPDATE bumps version: load profile at version V, save, version is V+1; profile_id stable.
      2. Concurrent in-place edits: admin A and B both load at V; A saves (now V+1), B saves with `If-Match: V` → 412.
      3. Same-day save routes to UPDATE-in-place: predecessor `effective_to` stays NULL; only one row exists for the triple.
      4. End-exclusive predecessor close: supersession with `newProfile.EffectiveFrom = X` produces predecessor `effective_to = X` (end-exclusive); query for "active on day X" returns the new row, not the predecessor.
    - **End-exclusive migration** (regression, Docker-gated): **3 scenarios**
      1. Pre-S22 closed row converts: `effective_to = '2026-03-31'` (inclusive) becomes `effective_to = '2026-04-01'` (exclusive); active-on-2026-03-31 query returns the row both before and after migration.
      2. Pre-S22 open row stays NULL: `effective_to IS NULL` stays NULL; row-version backfilled to 1.
      3. **Pre-S22 SegmentManifest replays byte-identical** (replay-determinism guarantee, Reviewer cycle-1 B3).
    - **`IEventStore` in-tx overload** (unit + regression): **3 scenarios**
      1. State-change site calls in-tx overload; outbox row appears in same tx; tx commit makes outbox row visible.
      2. Tx rollback removes outbox row; no event ever published.
      3. ETag/version backfill on existing profiles: post-migration GET returns version `1` ETag.
    - **Floor: 15 new tests.** Cells beyond floor add 1:1 against ADR-018's resolved scenarios.

## Scope Boundary

### In scope
- ADR-018 (supersedes ADR-004; amends ADR-017 D2 + D2.1).
- `outbox_events` table schema + indexes.
- `IEventStore` in-tx overload (per Q3) + outbox publisher (in-process, per Q2 once decided).
- Migration of all state-change emitting sites in Backend.Api + Payroll + External from post-commit `AppendAsync` to in-tx `AppendAsync(conn, tx, ...)`. Mechanical swap; touches ~12 files.
- Row-version column on `local_agreement_profiles` + audit-action `MODIFIED` extension + repository UPDATE-in-place path.
- End-exclusive `effective_to` migration on `local_agreement_profiles` + per-read-site predicate update.
- Pilot ETag/If-Match shape: `If-Match: <version>` (replaces ADR-017 D2.1's `If-Match: <profile_id>`).
- Replay-determinism proof in ADR-018 + regression test for pre-S22 manifest replay.
- Regression coverage per Q12's committed matrix.

### Out of scope (deferred to S23)
- ETag/If-Match propagation across `agreement_configs`, `position_overrides`, `wage_type_mappings`, `entitlement_configs` (D2.2).
- Q10's "shared helper vs per-repo replication" decision — depends on the four surfaces' lifecycle divergence; ADR-019 resolves.
- Q11's `agreement_configs` DRAFT/publish/clone three-race enumeration — sized as one of the larger S23 tasks.
- Outbox correlation columns on event payloads (`outbox_id` traceability) — additive, can land in either S22 (now) or S23. Default decision: ADR-018 leaves event payloads unchanged; outbox→events join uses `(stream_id, stream_version)` instead.

### Out of scope (deferred to later Phase 4)
- Versioned-history for non-dated boundary sources (Phase 4 X-1/X-2/X-3 sub-sprints — wage-type-mapping, entitlement-policy, employee-profile).
- UI/UX polish on optimistic-concurrency error rendering (Phase 5).
- Touching `local_configurations` legacy table (deprecated post-S21).
- Touching JWT, RBAC, or `OrgScopeValidator` — those layers stay unchanged.

## Planning Entrypoint

No implementation tasks defined yet. Sprint begins with the following **analysis-phase deliverables**:

1. **Architectural ADR** (ADR-018 — title TBD, e.g., "Transactional Outbox + Row-Version Optimistic Concurrency"). Answers Q1-Q12. Includes replay-determinism proof per Q8.
2. **Migration plan** — explicit handling of existing `local_agreement_profiles` rows (row-version backfill + end-exclusive conversion) + outbox table creation + per-read-site predicate update enumeration.
3. **Task decomposition** — ADR translated into `TASK-22NN` entries with domain agents, file scopes, and validation criteria.
4. **Entropy scan (Step 0a)** — completed 2026-05-03; findings recorded above.
5. **Plan review (Step 0b)** — cycle 1 completed 2026-05-03; results below.

Only after items 1-3 are Orchestrator-approved does Step 2 (Delegate) begin.

## Plan Review (Step 0b)

_Cycle 1 completed 2026-05-03. Trigger: MANDATORY (P1 architectural integrity + P3 auditability + P5 integration isolation + cross-domain Data Model + Backend API + Payroll + External + Test & QA + introduces new abstractions: outbox publisher, IEventStore in-tx overload, row-version column)._

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY |
| **External Codex** | invoked 2026-05-03 — cycle 1, 0 BLOCKER + 0 WARNING + 1 P3 NOTE (entropy-scan endpoint-count math, fixed) |
| **Internal Reviewer** | invoked 2026-05-03 — cycle 1, 4 BLOCKER + 6 WARNING + 4 NOTE |
| **BLOCKERs resolved before Step 1** | yes (2026-05-03, sprint-split + Q closures applied) |

### Findings — internal Reviewer (cycle 1)

**BLOCKERs:**
- **R-B1 (Q5)** — Option (a) "outbox row carries `stream_version` at enqueue time" reproduces S21 cycle-2 MVCC snapshot conflict. Listing it as viable contradicts the plan's pre-anchoring. **Resolution:** dropped from option list; Q5 pre-committed to (b); rationale stated.
- **R-B2 (Q3-Q5 coupling)** — Q3's `IEventStore` evolution options aren't independent of Q5; Q5's resolution forces option (b). **Resolution:** Q3 pre-committed to (b); coupling stated explicitly.
- **R-B3 (Q8 + manifest replay)** — End-exclusive migration needs explicit replay-determinism analysis for pre-S22 `SegmentManifest`s, parallel to ADR-017 D10. **Resolution:** Q8 expanded with replay-determinism proof structure; pre-committed to per-read-site predicate audit; Q12 test floor includes manifest replay regression.
- **R-B4 (Test matrix Q12)** — Missing the rolled-back-outbox-enqueue scenario (the inverse-direction guarantee that defines transactional-outbox correctness). **Resolution:** Q12 outbox category expanded from 4 to 5 scenarios; rollback case added.

**WARNINGs:**
- **R-W1 (ADR-004 framing)** — Plan called it a "name conflict" but ADR-004 is an unimplemented commitment. **Resolution:** Q11 added (ADR-018 supersedes ADR-004); framing reworded.
- **R-W2 (Q11 agreement_configs three races)** — D2.2 surface has DRAFT-edit + DRAFT→ACTIVE + ACTIVE→ARCHIVED races, not one. **Resolution:** scope split — D2.2 deferred to S23 entirely; the three-races concern lands in ADR-019.
- **R-W3 (Q10 ETag-shape divergence)** — Two distinct token shapes (profile_id-as-ETag vs version-only) need reconciliation. **Resolution:** S22 commits to `If-Match: <version>` (single-shape); S23/ADR-019 decides whether to thread through D2.2 surfaces.
- **R-W4 (Q2 outbox topology)** — State-change writes happen in Backend.Api AND Payroll AND External; per-service vs single-outbox decision unaddressed. **Resolution:** Q2 expanded to 3 options; ADR-018 names the cross-service write topology before commitment.
- **R-W5 (Outbox correlation on event payloads)** — Adding `outbox_id` is a payload schema change. **Resolution:** scope-boundary "Out of scope" decides — event payloads unchanged; outbox→events join uses `(stream_id, stream_version)`.
- **R-W6 (Q9 audit-action enum)** — Automatic in-place routing means `MODIFIED` audit-action; schema CHECK constraint extension. **Resolution:** Q9 pre-committed to automatic; audit-action extension committed; not deferred.

**NOTEs:**
- **R-N1 (Q4 outbox row debug)** — Add `stream_version BIGINT NULL` for post-publish join debugging. **Resolution:** layout updated.
- **R-N2 (Sprint-Goal sequencing)** — Profile row-version must complete before D2.2 propagation; only relevant if (α). **Resolution:** scope split — sequencing now across sprints (S22 then S23), not within.
- **R-N3 (D2.2 surface count consistency)** — No fifth surface lurking. **Resolution:** confirmed; carried into S23 scope.
- **R-N4 (D9c hydration predicate update)** — `effective_to >=` predicate becomes `>` under end-exclusive. **Resolution:** Q10 (read-site predicate update) added; D9c enumerated.

### Findings — external Codex (cycle 1)

- **C-N1 (P3 entropy-scan inconsistency)** — Endpoint-count math: "5-endpoint gap" with listed unauthenticated set "5 health + login = 6". **Resolution:** entropy-scan rewritten with verified count (4 health endpoints unauthenticated, RuleEngine's `/health` IS authenticated, plus login = 5).

### Convergence

- Reviewer's strong (β) recommendation matched the convergent evidence from R-B2/R-B3/R-W2/R-W3 (multiple seams within the trio). Sprint split applied 2026-05-03.
- Codex's P3 NOTE was a correctness fix on the entropy scan only; no impact on the plan's substantive direction.

Step 0b cycle 1 closed within the 2-cycle cap. 0 BLOCKERs remain in the post-split plan; ADR-018 drafting can begin.

## References

- [CLAUDE.md](../../CLAUDE.md) — priority order
- [ROADMAP.md](../../ROADMAP.md) — Phase 4 placement
- [SPRINT-21.md](SPRINT-21.md) — origin of the carry-forwards (D6 + Step-7a-D2)
- [SPRINT-23.md](SPRINT-23.md) — sibling sprint for D2.2 propagation (placeholder)
- [docs/knowledge-base/decisions/ADR-001-event-sourcing-postgresql-npgsql.md](../knowledge-base/decisions/ADR-001-event-sourcing-postgresql-npgsql.md) — current event store design
- [docs/knowledge-base/decisions/ADR-004-outbox-pattern-guaranteed-delivery.md](../knowledge-base/decisions/ADR-004-outbox-pattern-guaranteed-delivery.md) — _superseded by ADR-018_; approved S1 but never implemented
- [docs/knowledge-base/decisions/ADR-016-temporal-period-handling.md](../knowledge-base/decisions/ADR-016-temporal-period-handling.md) — D5b snapshot-at-calculation set; D10 replay-determinism pattern reused for Q8
- [docs/knowledge-base/decisions/ADR-017-local-agreement-configuration-as-a-profile.md](../knowledge-base/decisions/ADR-017-local-agreement-configuration-as-a-profile.md) — D2 close-then-insert window math (amended by ADR-018); D2.1 ETag pattern (amended by ADR-018 to row-version)
- [src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs](../../src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs) — current event-store implementation
- [src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs](../../src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs) — pilot ETag/If-Match implementation
- [src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs](../../src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs) — pilot PUT handler with concurrency precondition
