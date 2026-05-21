# ADR-026 — Audit Visibility Surface (Path C: Event-Projection per ADR-018 D13)

| Field | Value |
|-------|-------|
| **Status** | DRAFT (S38b TASK-38B-01 authorship; cycle-1 Step 7a dual-lens absorbed Codex B1+B2+B3 + Reviewer convergent W1+W3 + Reviewer-only W2: D2 dispatch mechanism reframed to S27-precedent endpoint-direct pattern; D3 event names corrected against actual EventSerializer + ~24 → ~34 mapper count; D4 introduces 3-tier visibility_scope enum resolving the NULL-target overload; D5 SQL updated to scope by visibility class. Cycle 2 dispatched to verify. Flips to ACCEPTED on cycle-2 clean.) |
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
    visibility_scope      TEXT         NOT NULL CHECK (visibility_scope IN ('TENANT_TARGETED', 'GLOBAL_TENANT_VISIBLE', 'GLOBAL_ADMIN_ONLY')), -- per-event class per D2/D4 (cycle-2 absorption of Codex B3 + Reviewer W3)
    target_org_id         TEXT         NULL REFERENCES organizations(org_id), -- the institution affected (required when visibility_scope='TENANT_TARGETED'; NULL for GLOBAL_*)
    target_resource_id    TEXT         NULL,                              -- the specific resource within the target_org (e.g., user_id, time_entry_id, agreement_config_id)
    actor_id              TEXT         NULL,                              -- the actor who triggered the event (may be NULL for system-generated events)
    actor_primary_org_id  TEXT         NULL,                              -- denormalized at projection time for fast scope-by-actor secondary query
    occurred_at           TIMESTAMPTZ  NOT NULL,                          -- event-payload timestamp (NOT request-middleware timestamp)
    correlation_id        UUID         NULL,                              -- inherited from event envelope per ADR-007
    details               JSONB        NOT NULL,                          -- event-specific subset of payload chosen by the per-event projection mapping (not the full event payload — projection-time selection per D2)
    projected_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),            -- when the projection row was written (forensic only; not used for ordering)
    CONSTRAINT chk_target_org_required_when_tenant CHECK (
        (visibility_scope = 'TENANT_TARGETED' AND target_org_id IS NOT NULL)
        OR (visibility_scope IN ('GLOBAL_TENANT_VISIBLE', 'GLOBAL_ADMIN_ONLY') AND target_org_id IS NULL)
    )
);

CREATE INDEX ix_audit_projection_target_org_time   ON audit_projection(target_org_id, occurred_at DESC) WHERE target_org_id IS NOT NULL;
CREATE INDEX ix_audit_projection_global_visible    ON audit_projection(occurred_at DESC) WHERE visibility_scope = 'GLOBAL_TENANT_VISIBLE';
CREATE INDEX ix_audit_projection_actor_org_time    ON audit_projection(actor_primary_org_id, occurred_at DESC) WHERE actor_primary_org_id IS NOT NULL;
CREATE INDEX ix_audit_projection_event_type_time   ON audit_projection(event_type, occurred_at DESC);
CREATE INDEX ix_audit_projection_outbox_id         ON audit_projection(outbox_id);  -- for backfill ordering per ADR-018 D13
```

**Why a separate table, not extending `audit_log`**: `audit_log` is the request-middleware row (one row per HTTP state-change). `audit_projection` is the event-side row (one row per audit-relevant domain event). The two surfaces serve different questions:
- `audit_log` answers "what HTTP requests happened and what was their outcome" — useful for HTTP-side debugging
- `audit_projection` answers "what domain events occurred affecting which tenant" — the tenant-scoped audit-visibility question

Both surfaces persist; the new `GET /api/admin/audit` endpoint queries `audit_projection` (per D5 below). Future ADR may unify or deprecate `audit_log`; out of scope here.

**Sync-in-tx per ADR-018 D13**: the per-event projection write happens INSIDE the same transaction as the source event's append-to-`events` + outbox emission. This guarantees read-your-write semantics for the audit-query surface (per S27 marquee D-test pattern). Repository helper `AuditProjectionRepository.InsertAsync(conn, tx, projection)` per (conn, tx) overload convention established S24+.

**Backfill seeder**: at S39 greenfield-baked migration, replay all existing audit-relevant events from `events` table (filtered by event_type ∈ audit-relevant set per D3) and write `audit_projection` rows. Idempotent on `event_id` UNIQUE constraint (re-running the seeder produces no duplicates). S40 ProjectionBackfillService extension per S27 precedent.

**No Phase B dependency** — system-design + security-correctness.

### D2 — Per-event projection: endpoint-direct dispatch (mirrors S27 precedent)

**Dispatch model (cycle-2 absorption of Codex B1 + Reviewer W1)**: ADR-026 follows the **S27 endpoint-direct pattern** literally — each state-change endpoint that emits an audit-relevant event additionally writes the projection row inline. There is **no event-handler bus**, **no DI-resolved auto-dispatch from the outbox publisher**. The real codebase reality (verified against `TimeEndpoints.cs:96-111` + `IOutboxEnqueue.cs:84` + `PostgresEventStore.cs:174` + `OutboxPublisher.cs:29`):

- `IOutboxEnqueue.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct)` returns the outbox row's `outbox_id` (ADR-018 D13 ordering column)
- `OutboxPublisher` is a `BackgroundService` that drains the outbox AFTER commit; it does NOT participate in the projection write
- The projection row is written by the endpoint inside the SAME `(conn, tx)` as the event append

**Mapper interface**: per-event pure mappers translate the event payload into an `AuditProjectionRow`. Mapper signature handles synchronous payload-only data extraction:

```csharp
public interface IAuditProjectionMapper<TEvent> where TEvent : class
{
    // Pure function: payload + caller-supplied context → row data.
    // Context carries actor_id + actor_primary_org_id + correlation_id supplied
    // by the calling endpoint (NOT looked up by the mapper).
    AuditProjectionRowData MapToRow(TEvent payload, AuditProjectionContext ctx);
}

public record AuditProjectionContext(
    string? ActorId,
    string? ActorPrimaryOrgId,    // endpoint resolves once via OrgScopeValidator
    Guid? CorrelationId,
    DateTime OccurredAt
);

public record AuditProjectionRowData(
    AuditVisibilityScope VisibilityScope,
    string? TargetOrgId,
    string? TargetResourceId,
    JsonDocument Details
);
```

**Endpoint-direct invocation pattern** (canonical example, mirrors S27 `TimeEndpoints.cs:96-111`):

```csharp
// inside an admin endpoint that emits ConfigBugCorrected:
await using var conn = await dataSource.OpenConnectionAsync(ct);
await using var tx = await conn.BeginTransactionAsync(ct);
// ... domain mutation + audit write per ADR-019 ...

var @event = new ConfigBugCorrected(...);
var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

// SAME tx — audit projection row write
var ctx = new AuditProjectionContext(actor.ActorId, actor.PrimaryOrgId, correlationId, occurredAt);
var rowData = configBugCorrectedMapper.MapToRow(@event, ctx);
await auditProjectionRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.GetType().Name, rowData, ct);

await tx.CommitAsync(ct);
```

**Mapper resolution**: DI-registered per type (`services.AddSingleton<IAuditProjectionMapper<ConfigBugCorrected>, ConfigBugCorrectedAuditMapper>()`). Endpoint receives the mapper via constructor injection or via a `IAuditProjectionMapperRegistry` lookup keyed by event type (S40 implementation refinement chooses; either matches the established DI patterns).

**Async lookups required by some events (Reviewer W1)**: events that need to resolve TargetOrgId via lookup (e.g., `MerarbejdeDiscretionary` needs employee's primary_org) — the **endpoint resolves the lookup BEFORE invoking the mapper** and passes the resolved value through the `Context` parameter (or — equivalently — the endpoint constructs `AuditProjectionRowData` directly without a mapper for events that need cross-table lookups). This keeps mappers pure + synchronous + testable without DB.

**No-mapping events**: events without an `IAuditProjectionMapper<T>` AND without endpoint-direct projection-row construction are NOT audit-relevant. This is the opt-in mechanism — adding a new event type to the audit surface = either writing a mapper (for payload-only mapping) OR constructing the row inline at the endpoint (for events needing context).

**No Phase B dependency**.

### D3 — Audit-relevant event inventory (S40 implementation scope; cycle-2 absorption of Codex B2 + Reviewer W2)

**Cycle-2 fix**: event names below verified against actual `EventSerializer.cs` registrations (58 events total at sprint-start). Previous draft cited fictive names (`OrganizationSoftDeleted`, `RoleAssigned`, `LocalConfigurationSet`, `PositionOverrideConfigCreated`, `ApprovalRequested`, `OvertimePreApprovalRequested`, `PayrollExported`) — replaced with canonical names. Soft-delete events that DON'T exist in EventSerializer (`OrganizationSoftDeleted`, `UserSoftDeleted`) are dropped from the retrofit inventory.

Each event's **visibility_scope** (per D1 enum) is declared explicitly. Three classes:

- **`TENANT_TARGETED`** — affects one institution; visible to that institution's LocalAdmin + GlobalAdmin
- **`GLOBAL_TENANT_VISIBLE`** — affects ALL institutions; visible to ALL LocalAdmins + GlobalAdmin (so tenant auditors can see global config publishes that may have changed their behavior)
- **`GLOBAL_ADMIN_ONLY`** — SaaS-operator-only events (the cross-tenant bypass per ADR-025 D5); LocalAdmins do NOT see these

**11 new events from S38 (ADR-024's 7 + ADR-025's 4)**:

| Event type | Visibility | TargetOrgId source | TargetResourceId source | Details JSON shape |
|------------|------------|---------------------|--------------------------|---------------------|
| `RoleConfigOverrideCreated` (ADR-024 D1) | GLOBAL_TENANT_VISIBLE | NULL | `(agreement_code, ok_version, employment_category)` tuple | `{agreement_code, ok_version, employment_category, ...override-fields}` |
| `RoleConfigOverrideUpdated` (D1) | GLOBAL_TENANT_VISIBLE | NULL | same | `{before, after}` diff |
| `RoleConfigOverrideSuperseded` (D1) | GLOBAL_TENANT_VISIBLE | NULL | same | `{previous_id, new_id, effective_from}` |
| `RoleConfigOverrideSoftDeleted` (D1) | GLOBAL_TENANT_VISIBLE | NULL | same | `{deleted_at, deleted_by}` |
| `MerarbejdeDiscretionary` (D2) | TENANT_TARGETED | endpoint resolves via employee→primary_org lookup BEFORE mapper invocation | employee_id | `{employee_id, period, hours, decision_pending}` |
| `OvertimeNecessityAcknowledged` (D7) | TENANT_TARGETED | endpoint resolves via employee→primary_org lookup | overtime_pre_approval_id | `{employee_id, necessity_reason, acknowledged_for_entries}` |
| `ConfigBugCorrected` (D6) | GLOBAL_TENANT_VISIBLE | NULL | configKey JSONB stringified | `{configSurface, configKey, classification, fromValue, toValue}` |
| `InstitutionProvisioned` (ADR-025 D2) | TENANT_TARGETED | new institution's org_id | institution_id | `{org_id, legal_name, subscription_tier, onboarded_by}` |
| `InstitutionDataExported` (D3 Part A) | TENANT_TARGETED | exported institution's org_id | export request id | `{org_id, requesting_actor_id, export_size_bytes}` |
| `UserPiiErased` (D3 Part B) | TENANT_TARGETED | user's primary_org_id (recorded BEFORE NULL-out) | user_id | `{user_id, erased_columns, erasure_token_hash}` |
| `CrossTenantReportAccessed` (D5) | **GLOBAL_ADMIN_ONLY** | NULL | report_id | `{report_type, parameter_hash}` |

**Pre-existing audit-relevant events for retrofit** (canonical names per EventSerializer.cs verified 2026-05-21):

- `OrganizationCreated` / `OrganizationUpdated` (S6) — TENANT_TARGETED; TargetOrgId = the org itself; resource_id = NULL
- `UserCreated` / `UserUpdated` (S6/S8) — TENANT_TARGETED; TargetOrgId resolved at endpoint via user→primary_org lookup; resource_id = user_id
- `RoleAssignmentGranted` / `RoleAssignmentRevoked` (S6) — TENANT_TARGETED; TargetOrgId = user's primary_org (endpoint lookup); resource_id = user_id
- `AgreementConfigCreated` / `AgreementConfigUpdated` / `AgreementConfigPublished` / `AgreementConfigArchived` / `AgreementConfigCloned` (S12/S25) — GLOBAL_TENANT_VISIBLE (global config affects all tenants); resource_id = config_id
- `LocalConfigurationChanged` (S7/S21) — TENANT_TARGETED; TargetOrgId = the local config's org_id; resource_id = NULL
- `PositionOverrideCreated/Updated/Activated/Deactivated` (S14/S25) — GLOBAL_TENANT_VISIBLE; resource_id = override_id
- `WageTypeMappingCreated/Updated/Deleted/Superseded` (S14/S25) — GLOBAL_TENANT_VISIBLE; resource_id = mapping_id
- `EntitlementConfigSeeded/Created/Superseded/SoftDeleted` (S15/S25) — GLOBAL_TENANT_VISIBLE; resource_id = config_id
- `LocalAgreementProfileChanged` (S21) — TENANT_TARGETED; TargetOrgId = the profile's org_id; resource_id = profile_id
- `PeriodSubmitted` / `PeriodApproved` / `PeriodRejected` / `PeriodEmployeeApproved` / `PeriodReopened` (S9/S25) — TENANT_TARGETED; TargetOrgId via employee→primary_org lookup; resource_id = period_id
- `OvertimePreApprovalCreated` / `OvertimePreApprovalApproved` / `OvertimePreApprovalRejected` (S17/S26) — TENANT_TARGETED; TargetOrgId via employee→primary_org lookup; resource_id = preapproval_id
- `RetroactiveCorrectionRequested` (S11) — TENANT_TARGETED; TargetOrgId via employee→primary_org lookup; resource_id = correction_id
- `PayrollExportGenerated` (S5+) — TENANT_TARGETED; TargetOrgId via employee→primary_org lookup (one row per export batch may span employees — S40 refinement picks: one projection row per (export, employee) OR projection row with `target_org_id = NULL` + scope=GLOBAL_TENANT_VISIBLE; user-decision deferred to S40)
- `UserAgreementCodeChanged` / `UserAgreementCodeSeeded` / `UserAgreementCodeSuperseded` (S33) — TENANT_TARGETED; TargetOrgId via user→primary_org lookup; resource_id = user_id
- `EmployeeProfileCreated/Updated/Superseded/SoftDeleted` (S31/S33) — TENANT_TARGETED; TargetOrgId via employee→primary_org lookup; resource_id = employee_id

Events NOT audit-relevant (no projection; reduces storage):

- `TimeEntryRegistered` (S1/S5) — too high volume; visible via `time_entries_projection` (S27); audit-relevant transitions captured at approval boundary
- `AbsenceRegistered` (S2) — too high volume; covered by `absences_projection` (S27)
- `TimerCheckedIn` / `TimerCheckedOut` (S9) — operational; not auditable user action
- `NormCheckCompleted` / `FlexBalanceUpdated` / `SupplementCalculated` / `OvertimeCalculated` / `PeriodCalculationCompleted` / `OvertimeCompensationApplied` (rule-engine internals) — calculation mechanics
- `RestPeriodViolationDetected` / `CompensatoryRestGranted` (S16) — compliance flags; future revisit if tenant audits demand
- `IntegrationDeliveryTracked` (S5) — outbox delivery telemetry
- `SegmentManifestCreated` (S20) — PCS internals
- `OvertimeBalanceAdjusted` / `EntitlementBalanceAdjusted` (S15/S17) — balance projections; future revisit
- `EmploymentProfile*` (S31/S33) — versioned config; pre-existing audit captured by upstream user_agreement_code lifecycle events

**S40 mapping authorship**: counted from inventory above ≈ **34 mappers** (11 new + ~23 retrofit) — Reviewer W2 was right that ~24 was understated. Each mapper is ~10-line file per S27 projection-mapper precedent. Total S40 LOC for mappers ≈ 340 lines.

**No Phase B dependency**.

### D4 — Visibility scope semantics + ADR-025 D5 bypass reconciliation (cycle-2 absorption of Codex B3 + Reviewer W3)

**Cycle-2 fix**: prior draft used binary nullable `target_org_id` (NULL = "GlobalAdmin only") which conflated two distinct semantics: (1) the cross-tenant bypass per ADR-025 D5 + (2) global config events that DO affect tenants and SHOULD be tenant-visible. The 3-tier `visibility_scope` enum (D1) resolves this.

**Three visibility classes**:

| Visibility | Who sees it | Examples |
|------------|-------------|----------|
| `TENANT_TARGETED` | Target tenant's LocalAdmin (when target_org_id ∈ requesting admin's subtree) + GlobalAdmin (always) | `UserCreated`, `InstitutionProvisioned`, `PeriodApproved`, `OvertimePreApprovalApproved`, `MerarbejdeDiscretionary` |
| `GLOBAL_TENANT_VISIBLE` | ALL LocalAdmins (every institution's auditors) + GlobalAdmin | `AgreementConfigPublished`, `WageTypeMappingCreated`, `ConfigBugCorrected`, `RoleConfigOverrideCreated`, `EntitlementConfigSeeded` — global config changes that affect every tenant's runtime behavior |
| `GLOBAL_ADMIN_ONLY` | GlobalAdmin only | `CrossTenantReportAccessed` (ADR-025 D5 single canonical bypass) |

**Why this resolves cycle-3's contradiction**: in the prior draft, "every audit-relevant event surfaces through `audit_log`" + "LocalAdmin filter excludes NULL target_org_id" couldn't both hold for global config events (they were NULL-targeted but tenant-impactful). The 3-tier enum lets D7 D-test 1 (event-coverage invariant) and D7 D-test 4 (cross-tenant leakage) both hold — different events have different visibility classes; the test asserts per-class semantics.

**ADR-025 D5 bypass preservation**: `CrossTenantReportAccessed` is the **only** event classified `GLOBAL_ADMIN_ONLY`. The single canonical scope-binding bypass per ADR-025 D5 §"Bypass exception" is the only LocalAdmin-invisible audit class. All other global events (`GLOBAL_TENANT_VISIBLE`) ARE tenant-visible. This preserves both:
- ADR-025 D5 contract (cross-tenant query is SaaS-operator-only auditable)
- Tenant audit completeness (global config publishes that may have changed tenant behavior ARE visible to tenant auditors investigating "why did our overtime threshold change last Tuesday")

**Compliance posture**: cross-tenant report access is logged and the SaaS operator owns the audit trail; per-tenant data subject access requests (Article 15) can request cross-tenant access details about their data — handled out-of-band by the SaaS operator per the data processor agreement, not via the LocalAdmin self-serve query surface.

**S40 implementation note**: the `visibility_scope` value is set by the per-event projection mapping (D2 + D3 inventory above). It is NOT derived from `target_org_id IS NULL` at query time — schema constraint at D1 (`chk_target_org_required_when_tenant`) enforces the 3-tier semantic at insert time.

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
     SQL (cycle-2 absorption: 3-tier visibility_scope semantic from D4):
       SELECT projection_id, event_id, event_type, visibility_scope,
              target_org_id, target_resource_id, actor_id,
              occurred_at, correlation_id, details
       FROM audit_projection
       WHERE 
         (
           @target_org_ids::TEXT[] IS NULL                                   -- GlobalAdmin: no scope restriction; sees all 3 classes
           OR (visibility_scope = 'TENANT_TARGETED' AND target_org_id = ANY(@target_org_ids))
              -- LocalAdmin: target_org_id IN subtree
           OR (visibility_scope = 'GLOBAL_TENANT_VISIBLE')
              -- LocalAdmin: also sees all global-config events affecting all tenants
           -- visibility_scope = 'GLOBAL_ADMIN_ONLY' rows EXCLUDED from LocalAdmin queries
           -- (the ADR-025 D5 cross-tenant bypass — SaaS-operator-only audit)
         )
         AND occurred_at BETWEEN @from AND @to
         AND (@event_type IS NULL OR event_type = @event_type)
         AND (@actor_id IS NULL OR actor_id = @actor_id)
         AND (@target_resource_id IS NULL OR target_resource_id = @target_resource_id)
       ORDER BY occurred_at DESC, projection_id DESC
       LIMIT 100 OFFSET (@page * 100);

     Index usage: partial index ix_audit_projection_target_org_time
     services the TENANT_TARGETED branch; partial index
     ix_audit_projection_global_visible services the
     GLOBAL_TENANT_VISIBLE branch; GlobalAdmin unrestricted scan
     falls back to ix_audit_projection_event_type_time (typical
     filter shape — N3 acknowledgment in cycle-1 Reviewer notes).
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

1. **Event-coverage invariant** — every event type registered in `EventSerializer` that is declared audit-relevant in `docs/operations/audit-projection-catalog.md` (new doc per D3 inventory) MUST have an `IAuditProjectionMapper<T>` registered in DI OR be invoked via endpoint-direct row construction (D2). Test enumerates EventSerializer + catalog + DI registrations + asserts equivalence per the catalog's `mapper_kind: "interface"|"endpoint-direct"` per-event declaration.
2. **Projection backfill idempotency** — running the backfill seeder twice produces the same `audit_projection` row count (no duplicates; `event_id` UNIQUE enforces).
3. **Sync-in-tx invariant** — for each audit-relevant event, the read-your-write D-test verifies `GET /api/admin/audit` returns the projection row immediately after the source-event-emitting endpoint completes (mirrors S27 marquee `PublisherStallReadYourWriteTests` pattern).
4. **Per-class visibility correctness** (replaces "cross-tenant leakage impossible" + "GlobalAdmin sees all" with explicit 3-tier coverage):
   - TENANT_TARGETED: 3 institutions × LocalAdmin querying → each sees only rows where target_org_id ∈ own subtree
   - GLOBAL_TENANT_VISIBLE: 3 institutions × LocalAdmin querying → ALL see the same set of global-config events
   - GLOBAL_ADMIN_ONLY: LocalAdmin queries get ZERO `CrossTenantReportAccessed` rows; GlobalAdmin gets all
5. **Schema constraint enforcement** — INSERT with mismatched visibility_scope + target_org_id (e.g., `TENANT_TARGETED` + NULL target_org_id) fails with `chk_target_org_required_when_tenant` CHECK violation. Negative D-test.

**No Phase B dependency**.

## Consequences

### S39 schema migration (adds to the 6 ADR-024+ADR-025 entries)

New ledger entry: `s39-d1-audit-projection-table` (this ADR's D1 schema). Total S39 schema ledger entries: **7** (6 from S38 + 1 from S38b).

### S40 cutover (adds to ADR-024+ADR-025 cutover scope)

- `AuditProjectionRepository.InsertAsync(conn, tx, projection)` + `QueryByOrgScopeAsync(targetOrgIds?, filter, page, ct)`
- `IAuditProjectionMapper<T>` interface (D2) + ~34 implementations across `audit-projection-mappers/` namespace (cycle-2 absorption: corrected from prior ~24 estimate per Reviewer W2 W-cycle1)
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
