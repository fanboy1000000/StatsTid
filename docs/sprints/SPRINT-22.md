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

## Migration Plan (Deliverable #2, 2026-05-03)

Per ADR-018, migration runs in two coordinated phases: schema migration (one transaction), code migration (sequential commits). The schema migration is gated by `schema_migrations` ledger so re-runs of `init.sql` are no-ops.

### Migration sequence

The deployment order matters because the publisher's `IEventStore.EnqueueAsync` overload depends on the `outbox_events` table existing. Production rollout (when applicable) follows this sequence; for the pre-production system in S22, all changes ship in one merge.

1. **Schema migration** — single DDL block in `init.sql`, idempotent on re-run.
2. **`IEventStore` interface evolution** — add `EnqueueAsync` overload (additive; no callers break).
3. **`PostgresEventStore` implementation** — implement `EnqueueAsync` against the new outbox table; existing `AppendAsync` becomes publisher-only post-S22.
4. **`OutboxPublisher` BackgroundService** — per-service hosted service, registered in each service's `Program.cs`.
5. **`LocalAgreementProfileRepository` row-version + UPDATE-in-place** — repository surface change; profile_id-as-ETag path retired.
6. **State-change site migration** — ~12 files swap `tx.Commit + AppendAsync` to `EnqueueAsync(conn, tx, ...) + tx.Commit`. Mechanical; touches Backend.Api endpoints + Payroll + External.
7. **`ConfigEndpoints` PUT rewrite** — version-based If-Match handling, MODIFIED audit action, EnqueueAsync.
8. **Frontend ETag helpers** — `parseVersionFromETag` + `formatVersionAsIfMatch` + `useConfig.ts` wiring.
9. **Test matrix + fixture updates** — D12's 16 new tests + ~10 call-site destructures across 4 existing fixtures.

### Schema migration (Step 1)

```sql
-- =========================================================================
-- S22 / ADR-018 schema migration
--
-- Order of operations within init.sql:
--   1. schema_migrations ledger table (CREATE IF NOT EXISTS, idempotent self-creation)
--   2. outbox_events table + indexes (CREATE IF NOT EXISTS, idempotent)
--   3. DO $$ block for the one-shot migration (guarded by ledger)
--      a. ALTER TABLE local_agreement_profiles ADD COLUMN version
--      b. UPDATE local_agreement_profiles SET effective_to = effective_to + INTERVAL '1 day'
--      c. ALTER TABLE local_agreement_profile_audit DROP+ADD CHECK CONSTRAINT
-- =========================================================================

-- Ledger first (cycle-3 review N-3 ordering invariant).
CREATE TABLE IF NOT EXISTS schema_migrations (
    migration_id  TEXT         PRIMARY KEY,
    applied_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    notes         TEXT         NULL
);

-- Outbox table.
CREATE TABLE IF NOT EXISTS outbox_events (
    outbox_id        BIGSERIAL    PRIMARY KEY,
    service_id       TEXT         NOT NULL,
    stream_id        TEXT         NOT NULL,
    event_id         UUID         NOT NULL UNIQUE,
    event_type       TEXT         NOT NULL,
    event_payload    JSONB        NOT NULL,
    correlation_id   TEXT         NULL,
    actor_id         TEXT         NULL,
    actor_role       TEXT         NULL,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    published_at     TIMESTAMPTZ  NULL,
    stream_version   INT          NULL,
    attempts         INT          NOT NULL DEFAULT 0,
    last_error       TEXT         NULL,
    last_attempt_at  TIMESTAMPTZ  NULL
);

CREATE INDEX IF NOT EXISTS idx_outbox_unpublished
    ON outbox_events (service_id, outbox_id)
    WHERE published_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_outbox_attempts
    ON outbox_events (service_id, attempts, last_attempt_at)
    WHERE published_at IS NULL AND attempts > 0;

CREATE INDEX IF NOT EXISTS idx_outbox_stream
    ON outbox_events (stream_id, outbox_id)
    WHERE published_at IS NULL;

-- One-shot guarded migration for D7 + D8 + D9.
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s22-d7-d8-d9', 'ADR-018: row-version + end-exclusive + MODIFIED audit action')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    -- D7: row-version column. Existing rows get DEFAULT 1 automatically.
    ALTER TABLE local_agreement_profiles
    ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

    -- D8: convert end-inclusive effective_to to end-exclusive.
    UPDATE local_agreement_profiles
    SET effective_to = effective_to + INTERVAL '1 day'
    WHERE effective_to IS NOT NULL;

    -- D9: extend audit-action enum to MODIFIED.
    ALTER TABLE local_agreement_profile_audit
    DROP CONSTRAINT IF EXISTS local_agreement_profile_audit_action_check;

    ALTER TABLE local_agreement_profile_audit
    ADD CONSTRAINT local_agreement_profile_audit_action_check
    CHECK (action IN ('CREATED', 'MODIFIED', 'SUPERSEDED', 'DEACTIVATED', 'MIGRATED_FROM_LEGACY'));
END
$$;
```

### Code migration sequencing (Steps 2-8)

| Step | File(s) | Change | Dependency |
|------|---------|--------|------------|
| 2 | `src/SharedKernel/StatsTid.SharedKernel/Events/IEventStore.cs` | Add `Task EnqueueAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string streamId, IDomainEvent @event, CancellationToken ct = default);` overload. | none |
| 3 | `src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs` | Implement `EnqueueAsync` — INSERT into `outbox_events` using caller's conn+tx. Generate `event_id = Guid.NewGuid()` at enqueue. | Step 1 (outbox_events table) + Step 2 (interface) |
| 4 | `src/Infrastructure/StatsTid.Infrastructure/Outbox/OutboxPublisher.cs` (new) | `BackgroundService`-implementing publisher. Polls `WHERE service_id = @ownServiceId AND published_at IS NULL`. ReadCommitted tx. event_id correlation on 23505. Per-service registration in each service's Program.cs (`builder.Services.AddHostedService<OutboxPublisher>()`). | Steps 1-3 |
| 4b | `src/Infrastructure/StatsTid.Infrastructure/Outbox/PublisherCorrelationException.cs` (new) | New exception type per ADR-018 D4. | Step 4 |
| 5a | `src/SharedKernel/StatsTid.SharedKernel/Models/LocalAgreementProfile.cs` | Add `Version` (long) init-only property. Read mappings updated in repository. | Step 1 (schema column) |
| 5b | `src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs` | `AcquireLockAsync` returns `(Guid ProfileId, long Version, DateOnly EffectiveFrom)?`. `SupersedeAndCreateAsync` returns `(Guid ProfileId, long Version)`. New private `UpdateInPlaceAsync` for same-day routing. End-exclusive predicate updates per D10 (`>=` → `>` in `GetActivationsInPeriodAsync`). `OptimisticConcurrencyException` reshaped to carry `expectedVersion`/`actualVersion` instead of profile_ids. `InvalidProfileSupersessionException` re-introduced. | Step 5a |
| 6 | 12 state-change-emitting endpoint sites | Mechanical swap: `await tx.CommitAsync(ct); await eventStore.AppendAsync(streamId, @event, ct);` → `await eventStore.EnqueueAsync(conn, tx, streamId, @event, ct); await tx.CommitAsync(ct);`. Files: `ConfigEndpoints.cs` (also Step 7), `AdminEndpoints.cs`, `AgreementConfigEndpoints.cs`, `PositionOverrideEndpoints.cs`, `WageTypeMappingEndpoints.cs`, `ApprovalEndpoints.cs`, `OvertimeEndpoints.cs`, `ComplianceEndpoints.cs`, `SkemaEndpoints.cs`, `TimerEndpoints.cs`, `TimeEndpoints.cs`, plus 1-2 sites in `Payroll` / `External` integrations. | Step 2 (interface) |
| 7 | `src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs` | PUT handler: parse `If-Match: "<version>"` (strip quotes), call `SupersedeAndCreateAsync` returning `(profileId, newVersion)`, audit action via `expectedCurrentVersion is null ? "CREATED" : (predecessor.EffectiveFrom == candidate.EffectiveFrom ? "MODIFIED" : "SUPERSEDED")`, set `ETag: "<newVersion>"` on response (quoted), call `EnqueueAsync` for `LocalAgreementProfileChanged` event. | Step 5b + Step 6 |
| 8a | `frontend/src/hooks/useConfig.ts` | Add `parseVersionFromETag` + `formatVersionAsIfMatch` helpers. Wire ETag header into PUT/GET flows. | Step 7 (API surface) |
| 8b | `frontend/src/components/config/ProfileEditor.tsx` | If-Match value comes from `useConfig` hook's tracked version state, not profile_id. | Step 8a |

### Test fixture updates

Existing test fixtures call `SupersedeAndCreateAsync` and assume return type `Guid`. Post-S22, the return is `(Guid ProfileId, long Version)`. Mechanical destructure update at:

| File | Approximate sites |
|------|------------------|
| `tests/StatsTid.Tests.Regression/Config/ProfileSupersessionTests.cs` | 3 sites (lines ~73, 87, 167 pre-S22) |
| `tests/StatsTid.Tests.Regression/Config/ProfileConcurrencyTokenTests.cs` | 4 sites (lines ~56, 71, 80, 95 pre-S22) |
| `tests/StatsTid.Tests.Regression/Config/ProfileAuditTests.cs` | 2 sites (lines ~136, 185 pre-S22) |
| `tests/StatsTid.Tests.Regression/Config/ProfileLegacyEventNonEmissionTests.cs` | 1 site (line ~68 pre-S22) |
| **Total** | **~10 destructure updates** |

Update pattern: `var id = await _repo.SupersedeAndCreateAsync(...)` → `var (id, version) = await _repo.SupersedeAndCreateAsync(...)`. Where the test asserts only the id, the version is discarded; where the test exercises the new optimistic-concurrency path, the version is asserted explicitly.

### Failure modes within migration

| Failure | Detection | Outcome |
|---------|-----------|---------|
| Schema migration partial failure (e.g., DDL syntax error in the DO block) | PostgreSQL transactional DDL — the entire DO block rolls back | Operator re-runs after fixing; ledger row was rolled back, so re-run is the first run |
| `+1 day` overflow on absurd dates (e.g., `effective_to = '9999-12-31'`) | PostgreSQL DATE arithmetic: `'9999-12-31' + INTERVAL '1 day'` errors with `date out of range` | Pre-migration: validate no closed row has `effective_to >= '9999-12-30'`. Post-S21 the seed has zero closed rows with dates near max-DATE; production data audit (per S21) confirms the same. |
| `outbox_events` write fails inside state-change tx | EnqueueAsync throws; state-change tx rolls back | Standard tx rollback; client receives 500 with the underlying error as a structured response; outbox row never visible to publisher |
| Publisher process crashes during D4 step 4 (event INSERT done, outbox UPDATE not done) | Restart re-attempts the row | event_id-correlated lookup (D4) recognizes the prior insert; marks outbox row published with the existing version |
| Publisher process crashes between D4 step 5 and step 6 | Restart re-attempts; outbox row already marked `published_at`; publisher skips it | No-op; the at-least-once guarantee holds |
| Test fixture not updated → compile error | `dotnet build` 0 errors check | Sprint cannot pass build; deliverable #3 task TASK-2208 includes the updates explicitly |

### D12 fixture set (16 named scenarios)

| # | Test class | Test name | Fixture seed | Assertion |
|---|------------|-----------|--------------|-----------|
| 1 | `OutboxPublisherTests` (Docker) | `HappyPath_EnqueueAndPublish` | state-change tx commits with EnqueueAsync; publisher polls | event_store row appears with assigned stream_version; outbox row has `published_at != NULL` and `stream_version != NULL` |
| 2 | `OutboxPublisherTests` (Docker) | `PublisherRestart_ResumesFromOldestUnpublished` | enqueue 5 rows; kill publisher mid-batch; restart | all 5 rows eventually published; ordering preserved within stream |
| 3 | `OutboxPublisherTests` (Docker) | `PerStreamFifo_OrderedPublishing` | enqueue A, B, C on same stream | events appear with consecutive stream_versions (V, V+1, V+2) |
| 4 | `OutboxPublisherTests` (Docker) | `CrossStreamConcurrency_NoInterference` | enqueue interleaved on streams X, Y | both publish concurrently up to publisher's parallelism setting |
| 5 | `OutboxPublisherTests` (Docker) | `RolledBackStateChange_DoesNotPublish` | open tx, EnqueueAsync, ROLLBACK | no outbox row visible; publisher never publishes; events table unchanged |
| 6 | `ProfileRowVersionTests` (Docker) | `InPlaceUpdate_BumpsVersion` | seed profile at version 1; PUT changes WeeklyNormHours | post-PUT version = 2; profile_id stable; effective_from unchanged |
| 7 | `ProfileRowVersionTests` (Docker) | `ConcurrentInPlace_StaleIfMatchReturns412_NoOutboxRow` | admin A and B both load at V; A saves (V+1); B saves with `If-Match: "V"` | B receives 412; outbox row count delta from B's save = 0 (cycle-3 N3 assertion) |
| 8 | `ProfileRowVersionTests` (Docker) | `SameDaySave_RoutesToUpdateInPlace` | seed profile with effective_from = today; save with same effective_from + changed value | one row exists for the triple; effective_to is NULL; audit-action MODIFIED |
| 9 | `ProfileRowVersionTests` (Docker) | `EndExclusiveSupersedeClosePredecessor` | seed profile at effective_from = X; save with effective_from = Y > X | predecessor.effective_to = Y (NOT Y-1); query "active on Y" returns the new row |
| 10 | `ProfileRowVersionTests` (Docker) | `SameDaySupersession_ThenInPlaceModification` (cycle-3 W4) | profile created today (CREATED); same-day SUPERSESSION with effective_from = today (??? — see test design note below) ... actually: profile created at effective_from = X-7; today = X; supersession PUT with effective_from = X (creates new row, audit SUPERSEDED); next PUT same-day at effective_from = X (UPDATE in place on the new row, audit MODIFIED) | audit chain: CREATED → SUPERSEDED → MODIFIED; final row's version = 2 |
| 11 | `EndExclusiveMigrationTests` (Docker) | `ClosedRow_ConvertsByOneDayShift` | pre-migration seed with end-inclusive `effective_to = '2026-03-31'`; run migration | post-migration `effective_to = '2026-04-01'`; "active on 2026-03-31" query returns the row both before and after |
| 12 | `EndExclusiveMigrationTests` (Docker) | `OpenRow_StaysNullWithVersion1` | pre-migration seed with `effective_to IS NULL`; run migration | post-migration `effective_to IS NULL` and `version = 1` |
| 13 | `EndExclusiveMigrationTests` (Docker) | `PreS22Manifest_ReplaysIdentical` (cycle-1 R-B3, cycle-3 W2-confirmed) | seed pre-S22-shape profile + create manifest using S21-era BuildPlanForLegacyCallers + run migration + replay manifest | replay output byte-identical to pre-migration evaluation; replay never reads the migrated row (verified via DB query log) |
| 14 | `EventStoreInTxTests` (Docker) | `EnqueueAsync_WritesOutboxRowInCallerTx` | open tx; EnqueueAsync; assert outbox row visible inside the same tx via SELECT | outbox row count delta within tx = 1; visibility outside tx = 0 until commit |
| 15 | `EventStoreInTxTests` (Docker) | `EnqueueAsync_RollsBackWithCallerTx` | open tx; EnqueueAsync; ROLLBACK; SELECT after | outbox row count delta = 0 |
| 16 | `EventStoreInTxTests` (Docker) | `VersionBackfill_ExistingProfileReturns1` | seed pre-S22 profile (no version column); run migration; GET profile | response ETag = `"1"`; profile.version = 1 |

**Floor: 16 tests.** Categories: 5 outbox + 5 profile + 3 migration + 3 IEventStore = 16.

Migration plan closed. Next: deliverable #3 (TASK-22NN decomposition).

## Task Log (Deliverable #3, 2026-05-03)

_Drafted from ADR-018 + Migration Plan; user-approval pending. All tasks are `not-started` until Step 2 (Delegate)._

### Task Index

| TASK | Domain / Agent | Phase | Title |
|------|----------------|-------|-------|
| TASK-2201 | Data Model | 1 | Schema migration: `outbox_events` + `schema_migrations` ledger + row-version on profiles + end-exclusive shift + audit-action MODIFIED |
| TASK-2202 | Data Model (extended into Infrastructure) | 1 | New `IOutboxEnqueue` interface in `StatsTid.Infrastructure.Outbox` (cycle-6 design — `IEventStore` in SharedKernel stays unchanged to preserve post-S19 assembly-graph purity) |
| TASK-2203 | Data Model (extended into Infrastructure) | 2 | `PostgresEventStore.EnqueueAsync` implementation + `OutboxPublisher` BackgroundService + `PublisherCorrelationException` |
| TASK-2204 | Data Model (extended into Infrastructure) | 2 | `LocalAgreementProfileRepository` row-version + UPDATE-in-place + end-exclusive predicates + `InvalidProfileSupersessionException` re-introduced |
| TASK-2205 | Backend API (cross-domain authorized) | 3 | `ConfigEndpoints` PUT rewrite — version-based If-Match + MODIFIED audit + EnqueueAsync |
| TASK-2206 | Backend API + Payroll Integration + External (cross-domain authorized) | 3 | State-change site migration — ~12 files swap post-commit `AppendAsync` to in-tx `EnqueueAsync` |
| TASK-2207 | UX | 3 | `useConfig.ts` ETag helpers + `ProfileEditor` version wiring |
| TASK-2208 | Test & QA | 4 | D12 16-scenario test matrix + ~10 fixture call-site destructure updates |

### Phase Ordering

- **Phase 1 (parallel-independent, worktree isolation)**: TASK-2201 (schema), TASK-2202 (interface). Both touch different surfaces.
- **Phase 2 (depends on Phase 1)**: TASK-2203 (publisher impl + new exception), TASK-2204 (repository surface change). Parallel via worktree isolation; both depend on Phase 1.
- **Phase 3 (parallel within Phase, depends on Phase 2)**: TASK-2205 (ConfigEndpoints — depends on TASK-2204), TASK-2206 (state-change site swap — depends on TASK-2202 + TASK-2203), TASK-2207 (frontend — depends on TASK-2205 API surface). Parallel via worktree isolation; merge conflicts on `ConfigEndpoints.cs` resolved by ordering TASK-2205 before TASK-2206 (TASK-2205 is one of the ~12 sites; TASK-2206 then sees ConfigEndpoints already-converted and skips it).
- **Phase 4 (sequential, depends on all production code)**: TASK-2208 (Test & QA matrix + fixture updates).
- **Phase 5 (Orchestrator)**: build/test validation, Step 5α Constraint Validator over all outputs, Step 5a Internal Reviewer (P1 + P3 + P5 + cross-domain + new abstractions — MANDATORY). **High-risk Step 5a override** also applies (P3 auditability + new authorization paths via the ETag rewrite + schema migration = three high-risk domains): external Codex review per task in addition to internal Reviewer. Step 7a sprint-end Codex review on full S22 diff against `07ffdbb` (ADR-018 ACCEPTED; current pre-implementation HEAD).

### Task Detail

#### TASK-2201 — Schema migration
**Agent**: Data Model
**Phase**: 1
**Files (write)**:
- `docker/postgres/init.sql` — additive: new `schema_migrations` ledger, `outbox_events` table + 3 indexes, DO $$ block for the one-shot S22 migration (version column + end-exclusive shift + MODIFIED CHECK constraint extension).

**Scope**:
- `schema_migrations` ledger table at the top of the schema migration block.
- `outbox_events` table per ADR-018 D1 + D4 row layout, including `event_id UUID NOT NULL UNIQUE`, `service_id`, `stream_version BIGINT NULL` (debug join), `attempts INT NOT NULL DEFAULT 0`.
- Three partial-conditional indexes: unpublished-by-service, retry-by-attempts, stream-locality.
- DO $$ block guards the destructive `+1 day` UPDATE behind ledger insert.

**Validation**: `dotnet build` clean (no compilation impact); `docker compose up postgres` succeeds; running `init.sql` twice produces no schema drift (idempotent).

**Cross-domain dependencies**: none. Subsequent tasks consume the schema.

#### TASK-2202 — `IOutboxEnqueue` interface (cycle-6 amended)
**Agent**: Data Model (extended scope into `src/Infrastructure/`; cross-domain authorized — interface lives with the Infrastructure assembly per ADR-018 D3 cycle-6 split)
**Phase**: 1
**Files (write)**:
- `src/Infrastructure/StatsTid.Infrastructure/Outbox/IOutboxEnqueue.cs` (new) — single-method interface:
  ```csharp
  namespace StatsTid.Infrastructure.Outbox;

  public interface IOutboxEnqueue
  {
      Task EnqueueAsync(
          NpgsqlConnection conn,
          NpgsqlTransaction tx,
          string streamId,
          IDomainEvent @event,
          CancellationToken ct = default);
  }
  ```

**Files NOT touched**:
- `src/SharedKernel/StatsTid.SharedKernel/Interfaces/IEventStore.cs` — STAYS UNCHANGED (cycle-6 amendment per ADR-018 D3). No new methods, no new package references. Adding `Npgsql` to `StatsTid.SharedKernel.csproj` would transitively leak into `StatsTid.RuleEngine.Api` via its `<ProjectReference>` to SharedKernel, regressing the post-S19 `b4fc670` Npgsql-free assembly-graph invariant.
- `StatsTid.SharedKernel.csproj` — NO new package references.

**Scope**: Interface-only addition in Infrastructure. Implementation lives in TASK-2203 (`PostgresEventStore` implements both `IEventStore` and `IOutboxEnqueue`).

**Validation**:
- `dotnet build` clean.
- `git status` shows ONLY the new `IOutboxEnqueue.cs` file modified; SharedKernel and its csproj are untouched.
- Verify no `using Npgsql;` was added to any SharedKernel file.

**Cross-domain dependencies**: TASK-2203 implements (`PostgresEventStore : IEventStore, IOutboxEnqueue` + dual-DI registration); TASK-2206 consumes (state-change sites inject `IOutboxEnqueue`, NOT `IEventStore`).

#### TASK-2203 — Publisher implementation + new exception (cycle-6 amended)
**Agent**: Data Model (extended scope into `src/Infrastructure/`; cross-domain authorized — repositories adjacent to events per S20/S21 precedent)
**Phase**: 2
**Files (write)**:
- `src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs` — implement BOTH interfaces: `class PostgresEventStore : IEventStore, IOutboxEnqueue`. The new `EnqueueAsync` (from `IOutboxEnqueue`) writes to `outbox_events` using caller's conn+tx; the existing `AppendAsync` (from `IEventStore`) becomes the publisher-only entry point.
- `src/Infrastructure/StatsTid.Infrastructure/Outbox/OutboxPublisher.cs` (new) — `BackgroundService`-implementing publisher per ADR-018 D2/D4. ReadCommitted tx, event_id correlation, per-stream FIFO, parallel cross-stream up to default 4. Injects `IEventStore` (publisher-side path is the canonical `AppendAsync`).
- `src/Infrastructure/StatsTid.Infrastructure/Outbox/PublisherCorrelationException.cs` (new) — exception per ADR-018 D4.
- `src/Backend/StatsTid.Backend.Api/Program.cs` + each service's `Program.cs` (`Payroll/Program.cs`, `External/Program.cs`) — DI registration per ADR-018 D3 dual-binding pattern:
  ```csharp
  builder.Services.AddSingleton<PostgresEventStore>();
  builder.Services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<PostgresEventStore>());
  builder.Services.AddSingleton<IOutboxEnqueue>(sp => sp.GetRequiredService<PostgresEventStore>());
  builder.Services.AddHostedService<OutboxPublisher>();
  ```
  Configuration injects the per-service `service_id` constant. Orchestrator's `Program.cs` does NOT register `OutboxPublisher` per ADR-018 D6 (Orchestrator MAY NOT write any stream).

**Scope**: Implementation of the publisher contract + per-service registration. Idempotency tested via D12 scenarios #1-5.

**Validation**: `dotnet build` clean; D12 OutboxPublisherTests scenarios #1-5 pass against running Docker; `dotnet test --filter "Category!=Docker"` shows the unit-level subset passes.

**Cross-domain dependencies**: TASK-2201 (outbox_events table) + TASK-2202 (interface). Consumed by TASK-2204 (LocalAgreementProfileChanged event) + TASK-2206 (all state-change sites).

#### TASK-2204 — `LocalAgreementProfileRepository` row-version + UPDATE-in-place
**Agent**: Data Model (extended scope into `src/Infrastructure/`; cross-domain authorized)
**Phase**: 2
**Files (write)**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/LocalAgreementProfile.cs` — add `Version` (long) init-only property.
- `src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs` — `AcquireLockAsync` returns `(Guid ProfileId, long Version, DateOnly EffectiveFrom)?`. `SupersedeAndCreateAsync` returns `(Guid ProfileId, long Version)`. New private `UpdateInPlaceAsync` for same-day routing. End-exclusive predicate updates per ADR-018 D10. `OptimisticConcurrencyException` reshaped (carries `expectedVersion`/`actualVersion`). `InvalidProfileSupersessionException` re-introduced.

**Scope**: Repository surface change + same-day routing logic + predicate updates. The end-exclusive predicate change applies to `GetActivationsInPeriodAsync` only (the other read sites are NULL-checks per D10).

**Validation**: `dotnet build` clean; all 18 existing Docker-gated profile tests pass with the destructure updates from TASK-2208 (sequenced). Specific D12 scenarios #6-9 pass (in-place update, concurrent 412, same-day routing, end-exclusive close).

**Cross-domain dependencies**: TASK-2201 (schema columns). Consumed by TASK-2205 (PUT rewrite).

#### TASK-2205 — `ConfigEndpoints` PUT rewrite
**Agent**: Backend API (cross-domain authorized)
**Phase**: 3
**Files (write)**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs` — PUT handler rewritten: parse `If-Match: "<version>"` (strip quotes per cycle-2 C3 fix), call `SupersedeAndCreateAsync` returning `(profileId, newVersion)`, audit action via three-way switch (CREATED / MODIFIED / SUPERSEDED), set `ETag: "<newVersion>"` quoted, call `EnqueueAsync` for `LocalAgreementProfileChanged` event in-tx (replaces post-commit `AppendAsync`).

**Scope**: Endpoint flow rewrite. ETag value extraction uses `parseVersionFromETag`-equivalent server-side helper (or inline regex). The audit-action three-way logic matches ADR-018 D9.

**Validation**: `dotnet build` clean; D12 scenarios #6-10 pass (the Profile category exercises the endpoint flow).

**Cross-domain dependencies**: TASK-2204 (repository surface) + TASK-2202 (interface). Consumed by TASK-2207 (frontend ETag wiring).

#### TASK-2206 — State-change site migration (~12 files)
**Agent**: Backend API + Payroll Integration + External (cross-domain authorized)
**Phase**: 3
**Files (write)**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/AgreementConfigEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/PositionOverrideEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/WageTypeMappingEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/TimerEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs` (entitlement events)
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` + `PayrollExportService.cs`
- `src/Integrations/StatsTid.Integrations.External/Services/IntegrationDeliveryService.cs`

**Scope**: Mechanical swap (cycle-6 amended for the split-interface design). Each call site changes from:
```csharp
async (..., IEventStore eventStore, ...) => {
    await tx.CommitAsync(ct);
    await eventStore.AppendAsync(streamId, @event, ct);
}
```
to:
```csharp
async (..., IOutboxEnqueue outbox, ...) => {
    await outbox.EnqueueAsync(conn, tx, streamId, @event, ct);
    await tx.CommitAsync(ct);
}
```
The DI parameter type changes from `IEventStore` to `IOutboxEnqueue`; the call site's local variable name shifts from `eventStore` to `outbox` for readability. No behavioral change beyond the path of arrival; downstream consumers see the same event shapes.

**Note**: any state-change site that reads events (calls `ReadStreamAsync` or `ReadAllAsync`) keeps its `IEventStore` injection — `IOutboxEnqueue` only covers the write path. State-change sites that BOTH read and write events inject both interfaces (the same `PostgresEventStore` instance behind both DI bindings per D3).

**Note**: `ConfigEndpoints.cs` is in TASK-2205, not here. TASK-2206 explicitly excludes it.

**Validation**: `dotnet build` clean; existing 517 unit + 35 plain regression tests pass without behavioral change (the events still get to the canonical store, just via the outbox).

**Cross-domain dependencies**: TASK-2202 (interface) + TASK-2203 (publisher must be running for events to arrive in events table). Consumed by no downstream task.

#### TASK-2207 — Frontend ETag helpers + `useConfig.ts` wiring
**Agent**: UX
**Phase**: 3
**Files (write)**:
- `frontend/src/hooks/useConfig.ts` — add `parseVersionFromETag` + `formatVersionAsIfMatch` helpers; wire ETag header into PUT/GET flows; track version state alongside profile state.
- `frontend/src/components/config/ProfileEditor.tsx` — If-Match value comes from `useConfig`'s tracked version, not profile_id.

**Scope**: Frontend ETag handling. Strip quotes on parse, add quotes on format. 412 response handling unchanged.

**Validation**: `npm test` passes (existing 48 vitest tests + maybe 2-3 new for the ETag helpers); ProfileEditor renders without errors against a mocked backend.

**Cross-domain dependencies**: TASK-2205 (API surface). UX agent's `frontend/**` scope covers all the new files.

#### TASK-2208 — D12 test matrix + fixture updates
**Agent**: Test & QA
**Phase**: 4
**Files (write)**:
- `tests/StatsTid.Tests.Regression/Outbox/OutboxPublisherTests.cs` (5 Docker-gated tests, scenarios #1-5)
- `tests/StatsTid.Tests.Regression/Config/ProfileRowVersionTests.cs` (5 Docker-gated tests, scenarios #6-10)
- `tests/StatsTid.Tests.Regression/Config/EndExclusiveMigrationTests.cs` (3 Docker-gated tests, scenarios #11-13)
- `tests/StatsTid.Tests.Regression/Outbox/EventStoreInTxTests.cs` (3 Docker-gated tests, scenarios #14-16)
- `tests/StatsTid.Tests.Regression/Config/ProfileSupersessionTests.cs` — destructure updates at 3 sites
- `tests/StatsTid.Tests.Regression/Config/ProfileConcurrencyTokenTests.cs` — destructure updates at 4 sites
- `tests/StatsTid.Tests.Regression/Config/ProfileAuditTests.cs` — destructure updates at 2 sites
- `tests/StatsTid.Tests.Regression/Config/ProfileLegacyEventNonEmissionTests.cs` — destructure updates at 1 site

**Scope**: 16 named scenarios per the D12 fixture set table + ~10 mechanical destructure updates. All Docker-gated tests use `TestFixtures.DockerHarness.StartAsync()`. Test class names + file paths match ADR-018 D12 verbatim.

**Validation**: `dotnet build` 0 errors; all Docker-gated tests properly trait-marked; existing 517 unit + 35 plain regression tests still pass (no regressions). After this task, total counts: 517 + 0 = 517 unit (no new unit tests; all D12 scenarios are Docker-gated since they exercise the schema), 35 + 0 = 35 plain regression, 18 (S21) + 16 (S22) = 34 Docker-gated profile + outbox + migration tests.

**Cross-domain dependencies**: depends on all production code (TASK-2201 through TASK-2207). Test & QA Agent's `tests/**` scope covers all new files.

### Risks & Watch-Points

- **TASK-2201 schema ordering** — the `schema_migrations` ledger table CREATE must precede the `DO $$` block. The init.sql edit places them in that order; reviewers should verify.
- **TASK-2203 publisher per-service registration** — each of Backend.Api, Payroll, External must register the publisher with its own `service_id`. Orchestrator does NOT register (per ADR-018 D6 invariant). Forgotten registration = events enqueued but never published; visible at first integration test.
- **TASK-2206 mechanical swap risk** — 12+ files; missing one means a state-change still uses post-commit `AppendAsync`. Reviewer cycle catches via grep audit (`grep -r "tx.CommitAsync" src/Backend/` should find no `AppendAsync` after).
- **TASK-2207 frontend version-state lifecycle** — `useConfig` must reset `version` on org/agreement change (otherwise stale `If-Match` from a different profile). Test scenario covers.
- **TASK-2208 destructure updates timing** — must run AFTER TASK-2204 lands (otherwise existing fixtures fail to compile); Phase 4 ordering enforces this.
- **High-risk Step 5a override** — S22 hits **P3 auditability + new authorization paths (ETag) + schema migration + new outbox publisher** = 4 high-risk categories (matching S21's count). External Codex review at Step 5a is mandatory per workflow; budget for one BLOCKER-fix cycle. Halt and prompt user after 2 BLOCKER cycles per workflow rule.

Task decomposition closed. Ready for user approval before Step 2 (Delegate).

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
