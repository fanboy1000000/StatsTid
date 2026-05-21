# ADR-026 — Audit Visibility Surface (Path C: Event-Projection per ADR-018 D13)

| Field | Value |
|-------|-------|
| **Status** | DRAFT (S38b TASK-38B-01 authorship; flips to ACCEPTED at TASK-38B-02 Step 7a-equivalent dual-lens). Previously PLANNED placeholder per S38 cycle-3 halt-and-prompt 2026-05-21. |
| **Sprint** | S38b (single-ADR sub-sprint absorbing the deferred ADR-025 D7) |
| **Domains** | Backend, Infrastructure, Security, Data Model, Frontend. |
| **Tags** | audit-visibility, tenant-scoping, event-projection, sync-in-tx-projection, audit_projection, scope-by-target, per-event-declaration, design-binding, phase-4e. |
| **Supersedes** | ADR-025 D7 (was deferred PLACEHOLDER at S38 cycle-3 halt-and-prompt; this ADR settles the design). |
| **Amends** | none — companion to ADR-024 + ADR-025 + ADR-013 amendment. |

## Context

ADR-025 D7 was deferred to this dedicated ADR per `feedback_thrash_defer_real_world.md` halt-and-prompt at S38 Step 7a cycle 3 (2026-05-21). The deferral resolved 3 cycles of new defects in the same audit-visibility architectural area:

- **Cycle 1**: D7 asserted existing `/api/admin/audit/` endpoint + `OrgScopeValidator.ValidateOrgScopeAsync` method — neither existed.
- **Cycle 2**: cycle-1 absorption introduced new fictive references (`OrgScopeValidator.GetAccessibleOrgsAsync` invented; `AuditLogRepository.GetByActorAsync` mis-named) AND surfaced the deeper substantive concern that `audit_log` row shape doesn't carry `target_org_id` / `target_resource_id` / `event_type` — JOIN-by-actor-primary-org misses operator/system actions from outside-actor.
- **Cycle 3**: cycle-2 absorption (D7 reframed as "minimal scope-by-actor + forward-pointer for schema extension") introduced an internal contradiction — D7 acknowledged the row-shape gap but then claimed event-type completeness D-test on the gap-limited shape.

User adjudicated path (C) **event-projection per ADR-018 D13** at S38b open 2026-05-21 over alternatives:

| Path | Status | Rejection rationale |
|------|--------|---------------------|
| (A) scope-by-actor | rejected | Operator/system actions invisible — explicit launch concern |
| (B) `audit_log` schema extension | rejected | Middleware retrofit touching every state-changing endpoint with per-endpoint target_org_id declarations; out of proportion |
| **(C) event-projection per ADR-018 D13** | **CHOSEN** | Aligns with S27 ProjectionBackfillService precedent; per-event explicit declarations cleaner than per-endpoint middleware; preserves event-log immutability per ADR-001 |
| (D) hybrid | rejected | Unnecessary complexity; C alone sufficient |

**Every decision below is binding for S40 implementation refinement.**

## Decisions

### D1 — `audit_projection` table (new sync-in-tx projection per ADR-018 D13)

Schema:

```sql
CREATE TABLE audit_projection (
    projection_id         UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id              UUID         NOT NULL UNIQUE,                  -- 1:1 with source event in `events` table
    outbox_id             BIGINT       NOT NULL,                          -- ordering column per ADR-018 D13 pattern
    event_type            TEXT         NOT NULL,                          -- canonical event type name (EventSerializer typeof().Name)
    target_org_id         TEXT         NULL REFERENCES organizations(org_id), -- the institution affected by this event (NULL = system-wide event with no tenant target)
    target_resource_id    TEXT         NULL,                              -- the specific resource within the target_org (e.g., user_id, time_entry_id, agreement_config_id)
    actor_id              TEXT         NULL,                              -- the actor who triggered the event (may be NULL for system-generated events)
    actor_primary_org_id  TEXT         NULL,                              -- denormalized at projection time for fast scope-by-actor secondary query
    occurred_at           TIMESTAMPTZ  NOT NULL,                          -- event-payload timestamp (NOT request-middleware timestamp)
    correlation_id        UUID         NULL,                              -- inherited from event envelope per ADR-007
    details               JSONB        NOT NULL,                          -- event-specific subset of payload chosen by the per-event projection mapping (not the full event payload — projection-time selection per D2)
    projected_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()             -- when the projection row was written (forensic only; not used for ordering)
);

CREATE INDEX ix_audit_projection_target_org_time  ON audit_projection(target_org_id, occurred_at DESC) WHERE target_org_id IS NOT NULL;
CREATE INDEX ix_audit_projection_actor_org_time   ON audit_projection(actor_primary_org_id, occurred_at DESC) WHERE actor_primary_org_id IS NOT NULL;
CREATE INDEX ix_audit_projection_event_type_time  ON audit_projection(event_type, occurred_at DESC);
CREATE INDEX ix_audit_projection_outbox_id        ON audit_projection(outbox_id);  -- for backfill ordering per ADR-018 D13
```

**Why a separate table, not extending `audit_log`**: `audit_log` is the request-middleware row (one row per HTTP state-change). `audit_projection` is the event-side row (one row per audit-relevant domain event). The two surfaces serve different questions:
- `audit_log` answers "what HTTP requests happened and what was their outcome" — useful for HTTP-side debugging
- `audit_projection` answers "what domain events occurred affecting which tenant" — the tenant-scoped audit-visibility question

Both surfaces persist; the new `GET /api/admin/audit` endpoint queries `audit_projection` (per D5 below). Future ADR may unify or deprecate `audit_log`; out of scope here.

**Sync-in-tx per ADR-018 D13**: the per-event projection write happens INSIDE the same transaction as the source event's append-to-`events` + outbox emission. This guarantees read-your-write semantics for the audit-query surface (per S27 marquee D-test pattern). Repository helper `AuditProjectionRepository.InsertAsync(conn, tx, projection)` per (conn, tx) overload convention established S24+.

**Backfill seeder**: at S39 greenfield-baked migration, replay all existing audit-relevant events from `events` table (filtered by event_type ∈ audit-relevant set per D3) and write `audit_projection` rows. Idempotent on `event_id` UNIQUE constraint (re-running the seeder produces no duplicates). S40 ProjectionBackfillService extension per S27 precedent.

**No Phase B dependency** — system-design + security-correctness.

### D2 — Per-event projection declaration mechanism

Each audit-relevant event type declares its projection mapping in code. The mapping is a function `(eventEnvelope, eventPayload) → AuditProjectionRow`. Declaration interface:

```csharp
public interface IAuditProjection<TEvent> where TEvent : class
{
    AuditProjectionRow Project(EventEnvelope envelope, TEvent payload);
}
```

Where `AuditProjectionRow` is:

```csharp
public record AuditProjectionRow(
    string EventType,           // typeof(TEvent).Name
    string? TargetOrgId,        // per-event extraction (e.g., for OrganizationCreated: payload.OrgId)
    string? TargetResourceId,   // per-event extraction (e.g., for UserCreated: payload.UserId)
    JsonDocument Details        // per-event subset of payload — JSON-serialized fields chosen by the projection author
);
```

The envelope provides: `event_id`, `outbox_id` (when known at projection time), `actor_id`, `actor_primary_org_id` (denormalized lookup at projection time), `occurred_at`, `correlation_id`. These are populated by the dispatch infrastructure, not per-event code.

**Registration**: each `IAuditProjection<T>` implementation registered in DI per established `IEventHandler<T>` pattern (S33 EmploymentProfile lifecycle handlers precedent). When the source event is appended via `OutboxPublisher.EnqueueAndReturnIdAsync` (ADR-018), the registered projection is invoked inline in the same transaction; the resulting `AuditProjectionRow` is INSERTed into `audit_projection` via the (conn, tx) overload.

**No-mapping = not audit-relevant**: events without a registered `IAuditProjection<T>` produce no `audit_projection` row. This is the opt-in mechanism — adding a new event type to the audit surface = writing one `IAuditProjection<NewEventType>` implementation.

**No Phase B dependency**.

### D3 — Audit-relevant event inventory (S40 implementation scope)

The 11 new events from S38 (ADR-024's 7 + ADR-025's 4) all get projections — they were authored explicitly because they need audit visibility:

| Event type | TargetOrgId source | TargetResourceId source | Details JSON shape |
|------------|---------------------|--------------------------|---------------------|
| `RoleConfigOverrideCreated` (ADR-024 D1) | implicit — global agreement-level, no tenant target | `(agreement_code, ok_version, employment_category)` tuple | `{agreement_code, ok_version, employment_category, ...override-fields}` |
| `RoleConfigOverrideUpdated` (D1) | same | same | `{before, after}` diff |
| `RoleConfigOverrideSuperseded` (D1) | same | same | `{previous_id, new_id, effective_from}` |
| `RoleConfigOverrideSoftDeleted` (D1) | same | same | `{deleted_at, deleted_by}` |
| `MerarbejdeDiscretionary` (D2) | employee's primary_org via lookup | employee_id | `{employee_id, period, hours, decision_pending}` |
| `OvertimeNecessityAcknowledged` (D7) | employee's primary_org | overtime_pre_approval_id | `{employee_id, necessity_reason, acknowledged_for_entries}` |
| `ConfigBugCorrected` (D6) | implicit — global, no tenant target | configKey JSONB stringified | `{configSurface, configKey, classification, fromValue, toValue}` |
| `InstitutionProvisioned` (ADR-025 D2) | new institution's org_id | institution_id | `{org_id, legal_name, subscription_tier, onboarded_by}` |
| `InstitutionDataExported` (D3 Part A) | exported institution's org_id | export request id | `{org_id, requesting_actor_id, export_size_bytes}` |
| `UserPiiErased` (D3 Part B) | user's primary_org_id (recorded BEFORE NULL-out) | user_id | `{user_id, erased_columns, erasure_token_hash}` |
| `CrossTenantReportAccessed` (D5) | NULL (no single target — bypass to D5 §"Bypass exception" below) | report_id | `{report_type, parameter_hash}` |

**Pre-existing audit-relevant events for retrofit**: backfill seeder also projects these pre-S38 event types known to be audit-relevant:

- `OrganizationCreated` / `OrganizationUpdated` / `OrganizationSoftDeleted` (S6) — TargetOrgId = the org itself
- `UserCreated` / `UserUpdated` / `UserSoftDeleted` (S6/S8) — TargetOrgId = user's primary_org
- `RoleAssigned` / `RoleRevoked` (S6) — TargetOrgId = user's primary_org
- `AgreementConfigPublished` / `AgreementConfigArchived` (S12/S25) — TargetOrgId = NULL (global)
- `LocalConfigurationSet` (S7/S21) — TargetOrgId = the local config's org_id
- `PositionOverrideConfigCreated/Updated/...` (S14/S25) — TargetOrgId = NULL (global)
- `WageTypeMappingCreated/Updated/...` (S14/S25) — TargetOrgId = NULL (global)
- `EntitlementConfigCreated/Updated/...` (S15/S25) — TargetOrgId = NULL (global)
- `LocalAgreementProfileSuperseded` / `LocalAgreementProfileArchived` (S21) — TargetOrgId = the profile's org_id
- `ApprovalRequested` / `ApprovalApproved` / `ApprovalRejected` (S9/S25) — TargetOrgId = employee's primary_org
- `OvertimePreApprovalRequested/Approved/Rejected` (S17/S26) — TargetOrgId = employee's primary_org
- `RetroactiveCorrectionRequested` (S11) — TargetOrgId = employee's primary_org
- `PayrollExported` (S5+) — TargetOrgId = employee's primary_org (one row per export batch)

Events NOT audit-relevant (no projection, reducing storage + scope by intent):

- `TimeEntryRegistered` / `TimeEntryEdited` (S1/S5) — too high volume + visible via `time_entries_projection` (S27); audit-relevant transitions are at the approval boundary, not per-entry
- `AbsenceRegistered` (S2) — same reasoning; covered by `absences_projection` (S27)
- `TimerStarted/Stopped` (S9) — operational, not audit-relevant at this granularity
- Calculation events (`CalculationRequested`, `CalculationCompleted`, etc.) — internal mechanics, not auditable user actions

**S40 mapping authorship** = ~24 `IAuditProjection<T>` implementations (11 new + ~13 retrofit). Each ~10-line file per S27 projection-mapper precedent.

**No Phase B dependency**.

### D4 — Cross-tenant bypass handling (ADR-025 D5 reconciliation)

ADR-025 D5 documents `/api/admin/reports/cross-tenant/` as the single canonical scope-binding bypass site (GlobalAdminOnly; aggregated data only, no individual PII). Each invocation emits `CrossTenantReportAccessed` event per D3 above. ADR-026 D3 maps this event with `TargetOrgId = NULL` because the report query touches multiple tenants by design.

**Visibility per D5 contract**:
- `CrossTenantReportAccessed` rows in `audit_projection` are visible to **GlobalAdmin queries only**. LocalAdmin queries via `GET /api/admin/audit` filter `WHERE target_org_id IN (subtree_org_ids)` — NULL `target_org_id` rows are excluded.
- GlobalAdmin queries via `GET /api/admin/audit` (with `actor_id` filter or unrestricted) see all rows including NULL-target ones.
- This preserves the single canonical bypass documentation: cross-tenant query is auditable but only at the SaaS-operator level. Tenant LocalAdmins cannot enumerate cross-tenant report invocations — by design, because that would itself violate the scope-binding boundary.

**Compliance posture**: cross-tenant report access is logged and the SaaS operator owns the audit trail; per-tenant data subject access requests (Article 15) can request cross-tenant access details about their data — handled out-of-band by the SaaS operator per the data processor agreement, not via the LocalAdmin self-serve query surface.

**No Phase B dependency**.

### D5 — `GET /api/admin/audit` endpoint design (tenant-scoped + GlobalAdmin variants)

Single endpoint with role-aware scope behavior:

```
GET /api/admin/audit
  ?from=<ISO8601 date>
  &to=<ISO8601 date>
  &event_type=<optional canonical event type name>
  &actor_id=<optional>
  &target_resource_id=<optional>
  &page=<optional 0-indexed integer>

Authorization: LocalAdminOrAbove

Implementation:
  1. Parse query params (from + to required; rest optional)
  2. Resolve requesting actor's accessible subtree:
     - GlobalAdmin → no scope restriction; queries all rows
     - LocalAdmin / HRBusinessPartner → OrgScopeValidator.GetAccessibleOrgsAsync(actorId)
                                          returns subtree org_ids (commissioned by this D5;
                                          new method per ADR-025 D7 deferral commissioning)
     - Other roles → 403
  3. Repository method (commissioned by this D5): 
     AuditProjectionRepository.QueryByOrgScopeAsync(
       IReadOnlyList<string>? targetOrgIds,           // null = no restriction (GlobalAdmin)
       AuditQueryFilter filter,
       int page,
       CancellationToken ct
     )
     SQL:
       SELECT projection_id, event_id, event_type, target_org_id,
              target_resource_id, actor_id, occurred_at, correlation_id, details
       FROM audit_projection
       WHERE 
         (@target_org_ids::TEXT[] IS NULL                          -- GlobalAdmin: no scope restriction
          OR target_org_id = ANY(@target_org_ids))                 -- LocalAdmin: target_org_id IN (subtree)
         AND occurred_at BETWEEN @from AND @to
         AND (@event_type IS NULL OR event_type = @event_type)
         AND (@actor_id IS NULL OR actor_id = @actor_id)
         AND (@target_resource_id IS NULL OR target_resource_id = @target_resource_id)
       ORDER BY occurred_at DESC, projection_id DESC
       LIMIT 100 OFFSET (@page * 100);
  4. Return { entries, totalCount, page, pageSize }
```

**Why scope-by-target-org-id, not scope-by-actor-primary-org**: D7 path (A) limitation eliminated. Operator/system actions affecting an institution from a non-tenant actor (e.g., GlobalAdmin's `InstitutionProvisioned` for institution X) ARE visible to institution X's LocalAdmin because `target_org_id = X` regardless of `actor.primary_org_id`.

**LocalAdmin can ALSO query by `actor_id`**: if they want to see their own actions or actions by other actors within their subtree, the optional `actor_id` filter combines with the subtree scope. SQL above handles both filters.

**Index usage**: `ix_audit_projection_target_org_time` (per D1) services the LocalAdmin path; `ix_audit_projection_event_type_time` services event-type filters; combined queries hit either depending on selectivity.

**No Phase B dependency**.

### D6 — Admin UI: `AuditLogView.tsx` page

New frontend page (LocalAdminOrAbove). Mirrors the established admin-page pattern (S25 admin pages precedent: list + filter chips + pagination). UI fields:

- Time range filter (from / to date pickers; default last 30 days)
- Event type filter (dropdown enumerated from EventSerializer registered audit-relevant set; "All" default)
- Actor filter (optional autocomplete by user display name → user_id)
- Target resource filter (free-text input)
- Result table columns: occurred_at | event_type | actor (display_name) | target_org (display_name) | target_resource_id | details (expandable JSON tooltip)
- Pagination footer (100/page; page navigation)

GlobalAdmin variant: org column not filtered; LocalAdmin variant: org column shows only subtree institutions.

No "scope-by-actor caveat banner" needed (path C resolves the operator/system-action visibility — the prior banner from cycle-3 absorbed text was a workaround for path A's limitation).

**No Phase B dependency**.

### D7 — Backfill seeder + Phase E continuous-validation tests

**Backfill**: at greenfield-baked migration (S39), `AuditProjectionBackfillService` replays events from `events` table filtered by `event_type ∈ audit-relevant set` (D3 enumeration) and writes `audit_projection` rows. Idempotent per `event_id` UNIQUE constraint. Console app + Backend.Api startup hook + D-test all delegate to the same service per S27 single-source-of-truth ProjectionBackfillService pattern.

**Phase E continuous-validation tests (S39 TASK-3905 additions)**:

1. **Event-coverage invariant** — every event type registered in EventSerializer that is declared audit-relevant in `docs/operations/audit-projection-catalog.md` (new doc per D3 inventory) MUST have an `IAuditProjection<T>` implementation registered in DI. Test enumerates EventSerializer + catalog + DI registrations + asserts equivalence.
2. **Projection backfill idempotency** — running the backfill seeder twice produces the same `audit_projection` row count (no duplicates; `event_id` UNIQUE enforces).
3. **Sync-in-tx invariant** — for each audit-relevant event, the read-your-write D-test verifies `GET /api/admin/audit` returns the projection row immediately after the source-event-emitting endpoint completes (mirrors S27 marquee D-test).
4. **Cross-tenant leakage impossible** — 3 institutions × LocalAdmin querying → each sees only `target_org_id IN (own subtree)`.
5. **GlobalAdmin sees all + NULL-target rows** — confirms D4 cross-tenant bypass visibility semantic.

**No Phase B dependency**.

## Consequences

### S39 schema migration (adds to the 6 ADR-024+ADR-025 entries)

New ledger entry: `s39-d1-audit-projection-table` (this ADR's D1 schema). Total S39 schema ledger entries: **7** (6 from S38 + 1 from S38b).

### S40 cutover (adds to ADR-024+ADR-025 cutover scope)

- `AuditProjectionRepository.InsertAsync(conn, tx, projection)` + `QueryByOrgScopeAsync(targetOrgIds?, filter, page, ct)`
- `IAuditProjection<T>` interface + ~24 implementations across `audit-projection-mappers/` namespace
- `OrgScopeValidator.GetAccessibleOrgsAsync(actorId)` — commissioned helper (carries forward from ADR-025 D7 cycle-2 commission; lives in the same place)
- DI wiring per established `IEventHandler<T>` pattern; OutboxPublisher invokes registered projections inline in source-event transaction
- `GET /api/admin/audit` endpoint (~80 LOC)
- `AuditLogView.tsx` admin page (~200 LOC)
- `AuditProjectionBackfillService` (~150 LOC; mirrors S27 ProjectionBackfillService pattern)
- Backend.Api startup auto-runs backfill on greenfield init (per S27 precedent)
- `docs/operations/audit-projection-catalog.md` — new doc enumerating audit-relevant event types + per-event projection mapping rationale
- `docs/SECURITY.md` updated for the D5 GlobalAdmin variant + LocalAdmin scope-by-target-org semantic

**No new event types** introduced by ADR-026 itself (path C uses existing events; per-event mappings + projection table are the additions). EventSerializer count post-S40 unchanged from S38 ACCEPTED state: **58 → 69**.

### S41 D-test matrix additions

5 D-tests from D7:
1. Event-coverage invariant
2. Backfill idempotency
3. Sync-in-tx read-your-write (marquee per audit-relevant event)
4. Cross-tenant leakage (3 institutions × LocalAdmin)
5. GlobalAdmin + NULL-target visibility

Plus existing S41 D-test scope per ADR-024 + ADR-025 ACCEPTED state.

### Companion ADRs

- **ADR-001** (event sourcing) — preserved: event log immutable; projection is the read surface
- **ADR-018 D13** (sync-in-tx canonical pattern) — extends to `audit_projection` (3rd projection table after `time_entries_projection` + `absences_projection` per S27)
- **ADR-024 D6** (`ConfigBugCorrected`) + D7 (`OvertimeNecessityAcknowledged`) + D2 (`MerarbejdeDiscretionary`) — all get projection mappings per D3
- **ADR-025 D5** (cross-tenant bypass) — reconciled per D4; `CrossTenantReportAccessed` projection has `TargetOrgId = NULL`; GlobalAdmin-only visibility preserved
- **ADR-025 D7** — superseded by this ADR (status PLACEHOLDER → SUPERSEDED-BY-ADR-026)

### Customer-go-live commitment

Per PROGRAM L279 + ADR-025 §Customer-go-live: launch-blocked on audit-visibility. ADR-026 ACCEPTED + S39+S40 implementation lands the actual code before any customer is provisioned. ADR-026 cannot defer past S39 — this sprint lands it inside the launch window.

## References

- PROGRAM-s36-s41-domain-correctness.md L142-151 (original ADR-025 D7 scope; superseded here)
- ADR-001 — event sourcing + event log immutability
- ADR-007 — correlation IDs (used in `audit_projection.correlation_id`)
- ADR-008 — materialized-path org hierarchy (used in `OrgScopeValidator.GetAccessibleOrgsAsync` subtree query)
- ADR-018 D13 — sync-in-tx projection canonical pattern; this ADR's primary architectural inheritance
- ADR-024 D1 + D2 + D6 + D7 — new events that need projection mappings
- ADR-025 D5 — cross-tenant bypass + `CrossTenantReportAccessed` event; reconciled per D4
- ADR-025 D7 — superseded by this ADR
- S27 (Sprint 27) — `time_entries_projection` + `absences_projection` + ProjectionBackfillService precedent
- `feedback_thrash_defer_real_world.md` — the discipline that produced this ADR's existence
- `AuditLoggingMiddleware.cs:37` — HTTP-side audit row (distinct surface; preserved unchanged)
- `AuditLogRepository.cs:42`/`:62` — `QueryByActorAsync` + `QueryByCorrelationAsync` (HTTP-side; unchanged)
