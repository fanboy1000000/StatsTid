# PLAN — Sprint 40: ADR-024 Sub-Sprint 1 (Schema + Repository + Events)

| Field | Value |
|-------|-------|
| **Sprint** | 40 |
| **Phase** | 4e (Phase D Implementation Sub-Sprint 1 per ADR-024 L234) |
| **Sprint type** | Implementation (schema + plumbing; no cutover code) |
| **Base commit** | `3a6f41a` (S39 close) |
| **Refinement** | `.claude/refinements/REFINEMENT-s40-adr024-schema-repo-events.md` (READY post-cycle-3) |
| **Predecessor refinements (cycle-trail)** | `REFINEMENT-s40-misscoped-cycle-trail.md` (cycle 1, 7 BLOCKERs → split per-ADR), `REFINEMENT-s40-adr024-too-big-cycle-trail.md` (cycle 2, 4 BLOCKERs → honor ADR-024 sub-sprint split) |
| **Sprint open date** | 2026-05-23 |
| **Task count** | 8 (TASK-4000..4007) |
| **Customer-go-live impact** | Sub-sprint discipline means audit-visibility (ADR-026) implementation now lands at S45+ rather than S42; honest cost of honoring ADR-author intent. |

## Sprint Goal

Lay the architectural foundation for ADR-024 (role-within-agreement modeling + correction policy + overtime authorization) without any rule-engine / payroll / endpoint / frontend changes. Schema tables + repository + event registrations + corrected seed values + Phase E bug_correction_history schema validation test. Subsequent ADR-024 cutover sprint (S41) dispatches against stable plumbing surface.

**Out of scope** (per refinement, explicit):
- ConfigResolutionService 4-layer extension (S41)
- OvertimeGovernanceRule + PayrollMappingService tri-state read (S41)
- D6 ConfigBugCorrected endpoint pattern (S41)
- D2 DISCRETIONARY workflow + admin UI (S41)
- Admin endpoints `/api/admin/role-config-overrides/{...}` (S41)
- Frontend `RoleConfigOverrideEditor.tsx` + Approval-page necessity-ack UI (S41)
- HK/PROSA `OvertimeRequiresPreApproval` seed flip + Bug #4 final resolution (S41 — needs necessity-ack endpoint first)
- Marquee D-test + per-agreement matrix + overtime-auth D-tests (S42)
- Phase E continuous-validation tests beyond bug_correction_history schema (S42 with cutover)
- WORKFLOW.md OK-version transition checklist + per-rule traceability (S42)

## Cycle-Trail Context

Refinement Step 4 ran 3 cycles to converge:
- Cycle 1 (7 BLOCKERs): originally bundled 3 ADRs in one sprint; misread binding ADRs from memory. User adjudication: split per-ADR.
- Cycle 2 (4 BLOCKERs): even per-ADR S40 = ADR-024-full was too big; ADR-024's own Consequences section splits into 3 sub-sprints. User adjudication: honor ADR-author's sub-sprint split.
- Cycle 3 (1 WARNING — mechanical SQL shorthand): clean otherwise. Absorbed inline.

Both superseded refinements preserved as `*-cycle-trail.md` artifacts. The lesson: dense ADR text deserves line-by-line reading before refinement drafting; "Sub-Sprint" naming from the ADR author is load-bearing not decorative.

## Phase Decomposition

All tasks Orchestrator-direct sequential. No worktrees. Init.sql is single-file so schema tasks must be sequential anyway.

| Phase | Tasks | Dispatch |
|-------|-------|----------|
| 0 | TASK-4000 | Sprint open plumbing |
| 1 | TASK-4001..4002 | Schema (sequential init.sql edits): role_config_overrides + overtime_pre_approvals extension |
| 2 | TASK-4003 | RoleConfigOverrideRepository (5th versioned-config pattern application) |
| 3 | TASK-4004 | EventSerializer wiring 7 new types |
| 4 | TASK-4005 | Greenfield seed: 8 rows (4 AC strata × 2 OK versions) |
| 5 | TASK-4006 | Phase E bug_correction_history schema validation test |
| 6 | TASK-4007 | Sprint close |

## Step 0a — Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-024 ACCEPTED at S38 close + projection disclaimer added at `a0e30ed`; no amendments this sprint. ADR-018 D3 (atomic outbox) + ADR-019 D2/D8 (admin-strict If-Match + audit version-transition) + ADR-020 D2 (3-case routing) + ADR-023 D8 (SoftDelete pattern) all referenced by repository pattern. |
| Pattern compliance | CLEAN | 5th versioned-config repository application after WTM/EntitlementConfig/EmployeeProfile/UserAgreementCode — pattern is mechanical. |
| Orphan detection | DEBT (carry-forward from S34/S35/S39) | 77 stale locked agent worktrees under `.claude/worktrees/`; S40 uses no worktrees so non-blocking. |
| Documentation drift | NONE-IDENTIFIED | QUALITY.md just updated at S39 close (Last updated: Sprint 39 (2026-05-23)). |
| Quality grade review | SCHEDULED | TASK-4007 close. SharedKernel (Events) typeof count moves 58 → 65 — mechanical, no grade movement expected. New "Domain Correctness" domain deferred to S42 when full Phase E suite lands. |
| Refinement disposition | READY | 3-cycle Step 4 trail to convergence. Cycle 1 → 2 → 3 with substantive user adjudications at cycle 1 and cycle 2. Cycle 3 = 1 mechanical WARNING absorbed inline. |
| Same-area thrash check | CLEAN | ADR amendment surface NOT touched per `a0e30ed` governance. Sprint-number refs in ADR-024 text read as projections. |

## Step 0b — Plan Review Trigger

**MANDATORY** per trigger criteria — sprint touches:

- **P3 (Event sourcing)**: 7 new event types is the largest single-sprint event addition since project inception.
- **P4 (Version correctness)**: new versioned-config repository follows ADR-020 D2 + ADR-023 D8 contract; cross-sprint replay determinism depends on dated lookup correctness.
- **P1 (Architectural integrity)**: cycle trail produced substantive scope refinements; plan review verifies the sub-sprint discipline holds.

Dispatch dual-lens (Codex external + Reviewer Agent internal) on this PLAN file before Phase 1 dispatches. Cycle-cap = 2 per lens per standard discipline.

**Escalation criterion**: if Step 0b cycle 1 surfaces a BLOCKER about ADR-024 details I still missed despite the 3-cycle refinement trail, that's the signal to halt and prompt user — refinement cycles 1-3 + plan review cycle 1 = 4 same-area opportunities to catch ADR-text issues.

## Architectural Constraints

_Checked at sprint close (TASK-4007)._

- [ ] **P1 — Architectural integrity** → Schema additions follow established versioned-config pattern; no architecture changes
- [ ] **P2 — Deterministic rule engine** → No rule code touched (cutover is S41)
- [ ] **P3 — Event sourcing / auditability** → 7 new event types registered; reflection-based coverage test passes; EventSerializer count 58 → 65
- [ ] **P4 — Version correctness** → RoleConfigOverrideRepository SupersedeAndCreate follows ADR-020 D2 3-case routing + ADR-023 D8 SoftDelete pattern
- [ ] **P5 — Integration isolation** → No outbox/publisher/consumer changes; new events register but emit only in test fixtures
- [ ] **P6 — Payroll integration** → No payroll code touched (cutover is S41)
- [ ] **P7 — Security / access control** → No new endpoints this sprint (cutover is S41); existing OrgScopeValidator + admin-strict If-Match patterns ready for S41 dispatch
- [ ] **P8 — CI/CD enforcement** → S39 quality gates (warn-as-error + .NET Analyzers security + gitleaks + vulnerable-package + smoke + vitest + lizard + coverage baseline) all pass on master HEAD post-merge
- [ ] **P9 — Usability / UX** → No UX changes (cutover is S41)

---

## Task Log

### Phase 0 — Sprint Open

#### TASK-4000 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-4000 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s40.md` (this file), `docs/sprints/SPRINT-40.md`, `docs/sprints/INDEX.md` provisional entry |

**Validation Criteria**:
- [ ] PLAN-s40.md filed with full task log + Step 0a + Step 0b sections
- [ ] SPRINT-40.md initial sprint-doc filed
- [ ] INDEX.md provisional Sprint 40 entry added
- [ ] Sprint-open commit through hook

---

### Phase 1 — Schema (TASK-4001..4002)

#### TASK-4001 — ADR-024 D1 schema: role_config_overrides + audit

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct (init.sql is shared infra) |
| **Components** | `docker/postgres/init.sql` (CREATE TABLE block + guarded ALTER block + ledger entry) |

**Schema spec** per ADR-024 L37-43 (D1 design) + L57-67 (D2 tri-state column placement):

```sql
CREATE TABLE IF NOT EXISTS role_config_overrides (
    override_id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    employment_category      TEXT         NOT NULL,
    agreement_code           TEXT         NOT NULL,
    ok_version               TEXT         NOT NULL,
    effective_from           DATE         NOT NULL,
    effective_to             DATE         NULL,  -- end-exclusive
    version                  BIGINT       NOT NULL DEFAULT 1,
    -- D2 tri-state
    merarbejde_compensation_right TEXT    NULL CHECK (merarbejde_compensation_right IN ('CONTRACTUAL', 'DISCRETIONARY', 'NONE')),
    -- 6 Boolean disablers per D1 L38 count (NULL means inherit from agreement_configs)
    -- Step 0b cycle 1 absorbed: added has_merarbejde to reach ADR-024 L38 "6 boolean disabler columns" count.
    -- Skips on_call_duty + call_in_work for now — those are S41 cutover discoverable per the chefkonsulent
    -- headline use case being merarbejde-loss not on-call-loss. Future ADR-024 amendment may extend.
    has_merarbejde           BOOLEAN      NULL,
    has_overtime             BOOLEAN      NULL,
    has_evening_supplement   BOOLEAN      NULL,
    has_night_supplement     BOOLEAN      NULL,
    has_weekend_supplement   BOOLEAN      NULL,
    has_holiday_supplement   BOOLEAN      NULL,
    -- Quantitative nullable overrides per D1 — explicit precision per S27 Step 7a cycle-2 lossy-NUMERIC absorption
    max_flex_balance         NUMERIC(7,2) NULL,
    flex_carryover_max       NUMERIC(7,2) NULL,
    norm_period_weeks        INT          NULL,
    weekly_norm_hours        NUMERIC(5,2) NULL,
    -- Audit metadata (matches existing agreement_configs convention)
    created_at               TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by               TEXT         NOT NULL,
    created_by_role          TEXT         NOT NULL
);

-- Partial unique index: only one ACTIVE row per natural key
CREATE UNIQUE INDEX IF NOT EXISTS idx_role_config_overrides_live
    ON role_config_overrides (employment_category, agreement_code, ok_version)
    WHERE effective_to IS NULL;

-- History unique index: one row per natural key per effective_from
CREATE UNIQUE INDEX IF NOT EXISTS idx_role_config_overrides_history
    ON role_config_overrides (employment_category, agreement_code, ok_version, effective_from);

-- Step 0b cycle 1 absorbed: audit column convention follows codebase pattern at
-- docker/postgres/init.sql:1116-1125 (agreement_config_audit): actor_id + actor_role + timestamp.
-- AppendAuditAsync overload mirrors AgreementConfigRepository.AppendAuditAsync 3-overload trio.
CREATE TABLE IF NOT EXISTS role_config_override_audit (
    audit_id                 BIGSERIAL    PRIMARY KEY,
    override_id              UUID         NOT NULL REFERENCES role_config_overrides(override_id),
    action                   TEXT         NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'SUPERSEDED', 'SOFT_DELETED')),
    version_before           BIGINT       NULL,  -- per ADR-019 D8
    version_after            BIGINT       NULL,
    previous_data            JSONB        NULL,
    new_data                 JSONB        NULL,
    actor_id                 TEXT         NOT NULL,
    actor_role               TEXT         NOT NULL,
    timestamp                TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_role_config_override_audit_override
    ON role_config_override_audit(override_id);
```

Plus the guarded ALTER block at end of init.sql for legacy DB upgrade (per S30/S31/S35 pattern). Plus ledger entry insertion: `INSERT INTO schema_migrations(migration_id, applied_at) VALUES ('s40-d1-role-config-overrides', NOW()) ON CONFLICT DO NOTHING;`.

**Validation**:
- [ ] `dotnet build StatsTid.sln` succeeds
- [ ] `docker compose up -d postgres` startup completes; init.sql runs without errors
- [ ] Direct SQL query: `SELECT table_name FROM information_schema.tables WHERE table_name LIKE 'role_config_override%'` returns 2 rows
- [ ] Both partial-unique-index + history-unique-index visible via `\d role_config_overrides`

#### TASK-4002 — ADR-024 D7 schema: overtime_pre_approvals extension + overtime_authorization_audit

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docker/postgres/init.sql` (ALTER TABLE + new audit table + ledger entry) |

**Schema spec** per ADR-024 L211-216 (D7 design):

```sql
-- ALTER existing overtime_pre_approvals table (S17 introduction; PK column is `id` per init.sql:1504)
ALTER TABLE IF EXISTS overtime_pre_approvals
    ADD COLUMN IF NOT EXISTS authorization_mode TEXT NOT NULL DEFAULT 'PRIOR_APPROVAL' CHECK (authorization_mode IN ('PRIOR_APPROVAL', 'POST_HOC_NECESSITY')),
    ADD COLUMN IF NOT EXISTS necessity_reason TEXT NULL,
    ADD COLUMN IF NOT EXISTS acknowledged_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS acknowledged_by TEXT NULL;

-- Step 0b cycle 1 absorbed: FK target is overtime_pre_approvals(id) not (pre_approval_id)
-- per init.sql:1504-1515. Audit column convention matches agreement_config_audit pattern.
CREATE TABLE IF NOT EXISTS overtime_authorization_audit (
    audit_id                 BIGSERIAL    PRIMARY KEY,
    pre_approval_id          UUID         NOT NULL REFERENCES overtime_pre_approvals(id),
    action                   TEXT         NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'NECESSITY_ACKNOWLEDGED')),
    version_before           BIGINT       NULL,
    version_after            BIGINT       NULL,
    previous_data            JSONB        NULL,
    new_data                 JSONB        NULL,
    actor_id                 TEXT         NOT NULL,
    actor_role               TEXT         NOT NULL,
    timestamp                TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_overtime_authorization_audit_pre_approval
    ON overtime_authorization_audit(pre_approval_id);
```

Plus ledger entry: `s40-d7-overtime-authorization-extension`.

**Validation**:
- [ ] init.sql runs without errors on fresh deploy AND on existing-DB upgrade (guarded ALTER block)
- [ ] `\d overtime_pre_approvals` shows 4 new columns
- [ ] `\d overtime_authorization_audit` shows the new audit table
- [ ] Existing overtime_pre_approvals rows (if any in seed) get DEFAULT `'PRIOR_APPROVAL'` for authorization_mode

---

### Phase 2 — Repository (TASK-4003)

#### TASK-4003 — RoleConfigOverrideRepository (5th versioned-config pattern)

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/RoleConfigOverrideRepository.cs` (new), `src/SharedKernel/StatsTid.SharedKernel/Models/RoleConfigOverride.cs` (new model record) |

**Pattern reference**: Mirror `src/Infrastructure/StatsTid.Infrastructure/AgreementConfigRepository.cs` (Step 0b cycle 1 absorbed — corrected from UserAgreementCodeRepository which doesn't carry `AppendAuditAsync`; AgreementConfigRepository has the 3-overload `AppendAuditAsync` trio at L684/707/735 matching the audit-bearing Pattern B repo shape needed here). Adapt:
- Table name: `role_config_overrides` instead of `agreement_configs`
- Natural key: `(employment_category, agreement_code, ok_version)`
- Surrogate PK: `override_id`
- Audit table: `role_config_override_audit`
- Versioned-config behavior from S29/S30/S31/S33/S34 still applies (SupersedeAndCreateAsync + SoftDeleteAsync); AgreementConfigRepository was the post-S25 admin-strict + audit pattern reference.

**Required methods** (Step 0b cycle 1 absorbed — return-shape uses Outcome discriminator like SaveUserAgreementCodeResult pattern):

```csharp
public sealed record SaveRoleConfigOverrideResult(Guid OverrideId, long Version, SaveOutcome Outcome);
public enum SaveOutcome { Created, UpdatedInPlace, Superseded }
```

- `Task<RoleConfigOverride?> GetCurrentAsync(string employmentCategory, string agreementCode, string okVersion, CancellationToken ct = default)` — live read
- `Task<RoleConfigOverride?> GetCurrentAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string employmentCategory, string agreementCode, string okVersion, CancellationToken ct = default)` — atomic-outbox overload per ADR-018 D3
- `Task<RoleConfigOverride?> GetByEmploymentCategoryAtAsync(string employmentCategory, string agreementCode, string okVersion, DateOnly asOfDate, CancellationToken ct = default)` — dated lookup for replay determinism per ADR-016 D10 + S33 ADR-023 D1 pattern
- `Task<SaveRoleConfigOverrideResult> SupersedeAndCreateAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string employmentCategory, string agreementCode, string okVersion, RoleConfigOverrideFields newFields, DateOnly effectiveFrom, string actorId, string actorRole, long? expectedVersion, CancellationToken ct = default)` — ADR-020 D2 3-case routing; result Outcome lets endpoint emit the correct event (Created → RoleConfigOverrideCreated; UpdatedInPlace → RoleConfigOverrideUpdated; Superseded → RoleConfigOverrideSuperseded)
- `Task SoftDeleteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string employmentCategory, string agreementCode, string okVersion, long expectedVersion, string actorId, string actorRole, CancellationToken ct = default)` — ADR-023 D8 SoftDelete
- `Task AppendAuditAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid overrideId, string action, long? versionBefore, long? versionAfter, JsonElement? previousData, JsonElement? newData, string actorId, string actorRole, CancellationToken ct = default)` — S24 Pattern B audit-bearing overload; (actorId, actorRole) matches codebase audit column convention

**Validation**:
- [ ] `dotnet build` succeeds
- [ ] Unit tests in `tests/StatsTid.Tests.Unit/Infrastructure/RoleConfigOverrideRepositoryTests.cs` exercise: Case A INSERT, Case B same-day UPDATE, Case C cross-day supersession, SoftDelete, GetCurrentAsync live read, GetByEmploymentCategoryAtAsync dated lookup
- [ ] Tests run against existing test DB harness (TestFixtures.DockerHarness pattern)

---

### Phase 3 — Events (TASK-4004)

#### TASK-4004 — Register 7 new event types in EventSerializer

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` (add 7 typeof entries), `src/SharedKernel/StatsTid.SharedKernel/Events/RoleConfigOverride*.cs` (new event records — 4 lifecycle), `src/SharedKernel/StatsTid.SharedKernel/Events/OvertimeNecessityAcknowledged.cs`, `src/SharedKernel/StatsTid.SharedKernel/Events/ConfigBugCorrected.cs`, `src/SharedKernel/StatsTid.SharedKernel/Events/MerarbejdeDiscretionary.cs` |

**Event classes to author** per existing codebase pattern (verified `src/SharedKernel/StatsTid.SharedKernel/Events/AgreementConfigArchived.cs`): `sealed class : DomainEventBase` with `required ... { get; init; }` properties + `public override string EventType => "..."` override. `EventId`, `OccurredAt`, `Version`, `ActorId`, `ActorRole`, `CorrelationId` are inherited from `DomainEventBase` — do NOT redeclare. Step 0b cycle 1 absorbed.

```csharp
// 1. RoleConfigOverrideCreated
public sealed class RoleConfigOverrideCreated : DomainEventBase {
    public override string EventType => "RoleConfigOverrideCreated";
    public required Guid OverrideId { get; init; }
    public required string EmploymentCategory { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public string? MerarbejdeCompensationRight { get; init; }  // nullable tri-state
    // 6 booleans + 4 quantitative overrides as nullable init properties
    public bool? HasMerarbejde { get; init; }
    public bool? HasOvertime { get; init; }
    public bool? HasEveningSupplement { get; init; }
    public bool? HasNightSupplement { get; init; }
    public bool? HasWeekendSupplement { get; init; }
    public bool? HasHolidaySupplement { get; init; }
    public decimal? MaxFlexBalance { get; init; }
    public decimal? FlexCarryoverMax { get; init; }
    public int? NormPeriodWeeks { get; init; }
    public decimal? WeeklyNormHours { get; init; }
}

// 2. RoleConfigOverrideUpdated — Case B same-day
public sealed class RoleConfigOverrideUpdated : DomainEventBase {
    public override string EventType => "RoleConfigOverrideUpdated";
    public required Guid OverrideId { get; init; }
    public required long VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
    // changed fields as nullable init properties (same shape as Created)
    public string? MerarbejdeCompensationRight { get; init; }
    // ... (6 booleans + 4 quant overrides as nullable init)
}

// 3. RoleConfigOverrideSuperseded — Case C cross-day
public sealed class RoleConfigOverrideSuperseded : DomainEventBase {
    public override string EventType => "RoleConfigOverrideSuperseded";
    public required Guid PredecessorOverrideId { get; init; }
    public required Guid SuccessorOverrideId { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    // new fields (same shape as Created)
}

// 4. RoleConfigOverrideSoftDeleted
public sealed class RoleConfigOverrideSoftDeleted : DomainEventBase {
    public override string EventType => "RoleConfigOverrideSoftDeleted";
    public required Guid OverrideId { get; init; }
    public required DateOnly EffectiveTo { get; init; }
}

// 5. OvertimeNecessityAcknowledged
public sealed class OvertimeNecessityAcknowledged : DomainEventBase {
    public override string EventType => "OvertimeNecessityAcknowledged";
    public required Guid PreApprovalId { get; init; }
    public required string NecessityReason { get; init; }
    public required IReadOnlyList<Guid> AcknowledgedForEntries { get; init; }
}

// 6. ConfigBugCorrected — D6 generalized
public sealed class ConfigBugCorrected : DomainEventBase {
    public override string EventType => "ConfigBugCorrected";
    public required string ConfigSurface { get; init; }  // 'agreement_configs', 'entitlement_configs', 'wage_type_mappings', 'position_override_configs', 'role_config_overrides'
    public required string ConfigKey { get; init; }
    public required string FromValue { get; init; }
    public required string ToValue { get; init; }
    public required string Source { get; init; }
    public required string Classifier { get; init; }
    public required string Action { get; init; }  // bug-fix-without-recompute / bug-fix-with-recompute / decision-recorded-fix-deferred / provisional-pending-phase-b
}

// 7. MerarbejdeDiscretionary — D2 flag event
public sealed class MerarbejdeDiscretionary : DomainEventBase {
    public override string EventType => "MerarbejdeDiscretionary";
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal MerarbejdeHours { get; init; }
    public required string EmploymentCategory { get; init; }
}
```

**EventSerializer registrations**: add 7 lines to the `EventSerializer._eventTypeMap` dictionary; count 58 → 65.

**Validation**:
- [ ] `dotnet build` succeeds
- [ ] Reflection-based coverage test (S18 TASK-1804) passes — all `DomainEventBase` descendants are registered
- [ ] Round-trip serialization test for each new event type passes

---

### Phase 4 — Seed (TASK-4005)

#### TASK-4005 — Greenfield seed: 8 rows for 4 AC strata × 2 OK versions

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docker/postgres/init.sql` (INSERT block after CREATE TABLE), `docs/references/agreement-source-register.md` (annotate role_config_overrides rows with `bug_correction_history.action = 'provisional-pending-phase-b'`) |

**Seed rows** per ADR-024 L46-50 (cycle-2 corrections):

```sql
INSERT INTO role_config_overrides (employment_category, agreement_code, ok_version, effective_from, merarbejde_compensation_right, created_by, created_by_role)
VALUES
    ('Fuldmægtig',       'AC', 'OK24', '0001-01-01', 'CONTRACTUAL',    'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Fuldmægtig',       'AC', 'OK26', '0001-01-01', 'CONTRACTUAL',    'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Specialkonsulent', 'AC', 'OK24', '0001-01-01', 'DISCRETIONARY',  'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Specialkonsulent', 'AC', 'OK26', '0001-01-01', 'DISCRETIONARY',  'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Chefkonsulent',    'AC', 'OK24', '0001-01-01', 'NONE',           'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Chefkonsulent',    'AC', 'OK26', '0001-01-01', 'NONE',           'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Kontorchef',       'AC', 'OK24', '0001-01-01', 'NONE',           'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Kontorchef',       'AC', 'OK26', '0001-01-01', 'NONE',           'SYSTEM_SEED', 'SYSTEM_SEED')
ON CONFLICT DO NOTHING;
```

`effective_from = '0001-01-01'` per the history-covering anchor convention (post-S33 EmployeeProfileSeeder pattern — seeded rows cover all-of-history; first PUT after seed triggers Case C cross-day cleanly).

`"Standard"` employment category is NOT seeded (ConfigResolutionService falls through to agreement_configs when no row matches — per ADR-024 L46).

**Source register annotations** per ADR-024 D3 schema (L102-113):
- For each of the 8 new rows, add a `bug_correction_history` entry to the source register:
  ```
  {date: 2026-05-24, from_value: "(no rows)", to_value: "<seeded value>", source: "S40 TASK-4005 + ADR-024 D1 D2 PROVISIONAL pending Phase B", commit: "<this S40 commit>", classifier: "Orchestrator (interim per S37 pattern)", was_agreed: PENDING, materially_wrong: PENDING_PHASE_B, action: "provisional-pending-phase-b"}
  ```

**Validation**:
- [ ] init.sql runs without errors; 8 rows present in `role_config_overrides` post-startup
- [ ] All 4 AC strata × 2 OK versions have correct tri-state values per ADR-024 L46-50
- [ ] No row for `"Standard"` employment category
- [ ] No rows for HK/PROSA/AC_RESEARCH/AC_TEACHING (per ADR-024 L51 "initial seed covers the 5 AC strata only")
- [ ] Source register `agreement-source-register.md` gains 8 `bug_correction_history` annotations referencing this sprint's commit hash (back-filled at sprint close)

---

### Phase 5 — Phase E partial (TASK-4006)

#### TASK-4006 — Phase E bug_correction_history schema validation test

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `tests/StatsTid.Tests.Regression/PhaseE/BugCorrectionHistorySchemaTests.cs` (new file) |

**Test scope** per ADR-024 L122 + D3 L102-113. Cutover-independent: only the schema-side validation (no rule-engine/payroll dependencies).

Test cases (~3-5 tests):
1. **Markdown parse** — `agreement-source-register.md` table rows parse without error; report row count
2. **9-field presence** — every `bug_correction_history` entry contains all 9 fields per L102-113 (date, from_value, to_value, source, commit, classifier, was_agreed, materially_wrong, action)
3. **Enum validity** — `was_agreed ∈ {YES, NO, PENDING}`; `materially_wrong ∈ {NO_PRE_LAUNCH, YES_PRE_LAUNCH_BUT_BROKEN, PENDING_S<NN>, YES_WITH_PAST_IMPACT, PENDING_PHASE_B}`; `action ∈ {bug-fix-without-recompute, bug-fix-with-recompute, decision-recorded-fix-deferred, provisional-pending-phase-b}`
4. **Commit hash resolves** — each `commit` field value resolves to a real git commit (via `git cat-file -e` or equivalent)
5. **Cross-reference consistency** — each row's `source` field that cites a sprint/task ID matches the project's sprint/task numbering convention (`S<NN> TASK-<NN><NN><NN>`)

Markdown parsing approach per refinement Open Q4: regex-locator table-format parsing with explicit diagnostic on failure ("couldn't parse SR row N at line X — verify table structure").

Test runs in `tests/StatsTid.Tests.Regression` with `[Trait("Category","Docker")]` if git-cat-file requires a real git repository; otherwise plain regression. Tentative: plain regression (no Docker needed for markdown parse + git cat-file).

**Validation**:
- [ ] Test file compiles + runs in CI
- [ ] All 5 tests pass against current source register state (including the 8 new role_config_overrides annotations from TASK-4005)
- [ ] Test count delta: +5 (in plain regression: 35 → 40)
- [ ] On purposely-malformed source-register row (test fixture), test fails with diagnostic message identifying the bad row

---

### Phase 6 — Sprint close (TASK-4007)

#### TASK-4007 — Sprint close

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/reviews/SPRINT-40-step7a-{codex,reviewer}.md` (Step 7a artifacts with `reviewed-against-commit:` line), `docs/sprints/SPRINT-40.md` (close sections), `docs/sprints/INDEX.md` (status flip + summary), `ROADMAP.md` (S40 completed-sprint row), `MEMORY.md` entry |

**Step 7a dispatch**: Codex external + Reviewer Agent internal on full S40 diff vs `3a6f41a`. Cycle-cap = 2 per lens. Review focus:
- Schema: 2 ledger entries, correct column types/CHECK constraints/indices
- Repository: full pattern compliance (mechanical check against UserAgreementCodeRepository as reference)
- EventSerializer: 58 → 65 typeof; reflection coverage test passes
- Seed: 8 rows, correct tri-state values per ADR-024 L46-50
- Phase E test: 5 tests passing; markdown parse robust

**Sprint close mechanics**:
- All 7 prior tasks marked complete
- Sprint-end HEAD commit hash backfilled in PLAN-s40 + SPRINT-40
- ROADMAP S40 row added to completed-sprints table + Phase Roadmap forward pointers shift to S41 (ADR-024 cutover)
- INDEX.md Sprint 40 row updated from in-progress → complete
- MEMORY.md entry with sprint summary
- QUALITY.md "Last updated" → Sprint 40 (2026-05-24 if multi-day, else 2026-05-23)
- `.claude/hooks/sprint-close-guard.ps1` passes (Step 7a artifacts present + `reviewed-against-commit:` line)

**Validation**:
- [ ] Both Step 7a verdicts CLEAN or APPROVED-WITH-NOTES (no BLOCKER)
- [ ] sprint-close-guard hook passes
- [ ] All architectural constraints P1-P9 checked off
- [ ] Sprint commit landed; working tree clean

---

## Forward Pointers

- **S41 = ADR-024 Sub-Sprint 2 (cutover)** per ADR-024 L245-254: ConfigResolutionService 4-layer extension + OvertimeGovernanceRule + PayrollMappingService MerarbejdeCompensationRight tri-state + admin endpoints `/api/admin/role-config-overrides/{...}` + new endpoint `POST /api/overtime-pre-approvals/{id}/acknowledge-necessity` + frontend `RoleConfigOverrideEditor.tsx` + Approval-page necessity-ack UI + HK/PROSA `OvertimeRequiresPreApproval` seed flip (Bug #4 final resolution) + D6 ConfigBugCorrected endpoint pattern. ~15-18 tasks.
- **S42 = ADR-024 Sub-Sprint 3 (D-tests + Phase E completion)** per ADR-024 L256-267: marquee chefkonsulent past-period replay determinism + per-agreement matrix + overtime auth D-tests + Phase E completion (seed-parity + unknown-unknown + DRAFT-OK source-cite enforcement) + WORKFLOW.md OK-version transition checklist + per-rule traceability + QUALITY.md Domain Correctness category. ~10-12 tasks.
- **S43+ = ADR-025 sub-sprints** (Multi-Tenant Operational Concerns; per ADR-025 own sub-sprint enumeration)
- **S46+ = ADR-026 sub-sprints** (Audit Visibility Surface; per ADR-026 D7 schema → cutover → D-tests split)
- **Customer-go-live commitment**: now slides by significantly more than +1 sprint vs S38b's original projection. Honest cost of sub-sprint discipline + the cycle-trail lesson that ADR-author sub-sprint splits are load-bearing not decorative.

---

## Cycle Trail Note

3-cycle refinement Step 4 trail produced this scope:
- Cycle 1 (7 BLOCKERs): originally bundled 3 ADRs → user adjudication: split per-ADR
- Cycle 2 (4 BLOCKERs): ADR-024-full too big → user adjudication: honor ADR-024 sub-sprint split
- Cycle 3 (1 mechanical WARNING): scope clean; absorbed inline

**Lesson for future ADR-implementation sprints**: dense ADR text deserves line-by-line reading BEFORE refinement drafting. "Sub-Sprint" naming from the ADR author is load-bearing, not decorative. Schema + repo + events alone is a legitimate full-sprint scope when the ADR is dense.

Both superseded refinements preserved at `.claude/refinements/REFINEMENT-s40-misscoped-cycle-trail.md` + `REFINEMENT-s40-adr024-too-big-cycle-trail.md` for future-sprint reference.
