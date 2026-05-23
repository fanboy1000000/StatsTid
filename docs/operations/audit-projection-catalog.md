# Audit Projection Catalog

> **Sprint**: S43 / TASK-4305 (ADR-026 Sub-Sprint 1 plumbing)
> **Source**: ADR-026 D3 inventory (`docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md`)
> **Status**: Initial draft — `mapper_kind` + `sprint_landed` blank / TBD pending Sub-Sprint 2 mapper authorship

## Purpose

Tabulates every audit-relevant event in the system with its expected projection shape — the canonical "what gets a row in `audit_projection`" reference. Sub-Sprint 2 (S44) ships ~53 `IAuditProjectionMapper<T>` implementations following this table; Sub-Sprint 3 (S45) Phase E **Test #1** parses this markdown table at test time and asserts the catalog ↔ DI registrations ↔ EventSerializer typeofs are all in lockstep (per ADR-026 D7 invariant: any drift between the three breaks Test #1).

## Locked column structure

The 7-column markdown table below is **structurally pinned** — Sub-Sprint 3 Test #1's parser depends on column order + exact header text. Schema changes require a coordinated update across this file + the parser. **Do not reorder columns.**

| Column | Semantic | Allowed values |
|--------|----------|----------------|
| `event_type` | Canonical EventSerializer typeof().Name | exact match to `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` registrations |
| `visibility_scope` | Per-ADR-026 D1+D4 visibility classification | one of `TENANT_TARGETED` / `GLOBAL_TENANT_VISIBLE` / `GLOBAL_ADMIN_ONLY` |
| `target_org_resolution` | How the endpoint resolves `target_org_id` before invoking the mapper | `NULL` (for GLOBAL_*) OR a lookup recipe (`employee → users.primary_org_id`, etc.) |
| `target_resource_id` | Natural key string identifying the affected resource | `user_id`, `config_id`, `override_id`, etc., or `NULL` |
| `details_shape` | JSONB shape sketch for the `details` column | shape signature; full schemas live in ADR-026 D3 |
| `mapper_kind` | `interface` (payload-only mapper) OR `endpoint-direct` (mapper needs cross-table lookup so endpoint constructs row data) OR `TBD-*` (Sub-Sprint 2 decides) | populated at Sub-Sprint 2 mapper authorship time |
| `sprint_landed` | Commit hash where the mapper landed | populated at Sub-Sprint 2 close (or later) |

## TBD marker taxonomy (S44 TASK-4404)

Catalog rows whose `mapper_kind` is `TBD` (plain) are simply pending mapper authorship in S44b/c. Rows with a `TBD-<suffix>` marker carry an architectural-question semantic that must be resolved BEFORE mapper authorship — Phase E Test #1 (S45+) treats each suffix as deferred-with-named-follow-up rather than "ready to assert". Each suffix has a named follow-up sprint or sprint family.

### Active TBD suffix markers

1. **`TBD-cross-process-deferred`** (1 row) — Event emitted from a cross-process boundary (`src/Integrations/**`) via `IEventStore.AppendAsync` with self-managed connection. ADR-026 D2 sync-in-tx contract requires `(conn, tx)` + `IOutboxEnqueue.EnqueueAndReturnIdAsync` shape; the emitting service lacks the DI surface to participate.
   - Single row: `RetroactiveCorrectionRequested` at `src/Integrations/StatsTid.Integrations.Payroll/Services/RetroactiveCorrectionService.cs:222`.
   - Named follow-up: **S44-cross-process sprint** (sized as ~1 event scope; solves Payroll DbConnectionFactory introduction + atomic-tx-spanning-process-boundary architectural question holistically).
   - Originated at S43 Step 4 cycle 1 dual-lens (caller-census discipline catch); resolved direction at S44 Step 4 cycle 1 (option iii: defer).

2. **`TBD-adr025-implementation-pending`** (4 rows) — Event class referenced in ADR-026 D3 inventory but NOT registered in EventSerializer; the underlying feature (ADR-025 multi-tenant operational concerns) was DESIGN-ONLY at S38 with no endpoints, RBAC, or frontend yet implemented.
   - Rows: `InstitutionProvisioned`, `InstitutionDataExported`, `UserPiiErased`, `CrossTenantReportAccessed`.
   - Named follow-up: dedicated post-launch ADR-025-feature-implementation sprints — each event corresponds to a feature with endpoint + RBAC + frontend surface.
   - 4× rot risk (Reviewer NOTE absorption): each future ADR-025 sprint MUST include "remove TBD marker from catalog row" as an explicit acceptance criterion. Phase E Test #1 will fail loudly if rows remain TBD-marked when the underlying event-class registration lands.

3. **`TBD-defined-but-unemitted`** (1 row) — Event class IS defined in `src/SharedKernel/StatsTid.SharedKernel/Events/` + registered in EventSerializer, but caller-census surfaced **zero production emit sites** in `src/`. Vestigial S22-era class.
   - Single row: `PayrollExportGenerated`.
   - Named follow-up: **S44f** — decides between (a) add emit-site code in the payroll-export pipeline + mapper, OR (b) delete the vestigial event class + EventSerializer entry + catalog row.

### Phase E Test #1 parser contract (S45+)

The future Test #1 (catalog ↔ DI registrations ↔ EventSerializer parity) parses this markdown table and treats `mapper_kind` cells per these rules:
- `interface` or `endpoint-direct` → **assert**: matching `IAuditProjectionMapper<T>` DI registration must exist; matching `RegisteredAuditEventType` marker must exist; EventSerializer must register the event class.
- `TBD` (plain) → **defer**: not yet asserted; future sub-sprint will populate.
- `TBD-<suffix>` → **defer with named follow-up**: not yet asserted; the suffix names the resolution sprint. Test passes; resolution-sprint acceptance criterion is responsible for marker removal.

## Pre-existing naming drifts (informational)

- **`ix_*` vs `idx_*`** — ADR-026 D5 SQL block uses `ix_audit_projection_*` index names; init.sql:2057-2077 ships them as `idx_audit_projection_*` (matches codebase convention of 75/75 existing indexes). Refinement + caller-census + this catalog all cite the `idx_*` names per init.sql. Reviewer N4 (S44 Step 0b cycle 1) flagged this as pre-existing; doc-only fix when ADR-026 next opens.

## Catalog

### New events from S38 ADRs (11)

| event_type | visibility_scope | target_org_resolution | target_resource_id | details_shape | mapper_kind | sprint_landed |
|------------|------------------|-----------------------|--------------------|---------------|-------------|---------------|
| `RoleConfigOverrideCreated` | GLOBAL_TENANT_VISIBLE | NULL | `(agreement_code, ok_version, employment_category)` tuple | `{agreement_code, ok_version, employment_category, ...override-fields}` | TBD | |
| `RoleConfigOverrideUpdated` | GLOBAL_TENANT_VISIBLE | NULL | same | `{before, after}` diff | TBD | |
| `RoleConfigOverrideSuperseded` | GLOBAL_TENANT_VISIBLE | NULL | same | `{previous_id, new_id, effective_from}` | TBD | |
| `RoleConfigOverrideSoftDeleted` | GLOBAL_TENANT_VISIBLE | NULL | same | `{deleted_at, deleted_by}` | TBD | |
| `MerarbejdeDiscretionary` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employee_id, period, hours, decision_pending}` | TBD | |
| `OvertimeNecessityAcknowledged` | TENANT_TARGETED | `employee → users.primary_org_id` | `overtime_pre_approval_id` | `{employee_id, necessity_reason, acknowledged_for_entries}` | TBD | |
| `ConfigBugCorrected` | GLOBAL_TENANT_VISIBLE | NULL | configKey JSONB stringified | `{configSurface, configKey, classification, fromValue, toValue}` | TBD | |
| `InstitutionProvisioned` | TENANT_TARGETED | new institution's `org_id` | `institution_id` | `{org_id, legal_name, subscription_tier, onboarded_by}` | **TBD-adr025-implementation-pending** | |
| `InstitutionDataExported` | TENANT_TARGETED | exported institution's `org_id` | export request id | `{org_id, requesting_actor_id, export_size_bytes}` | **TBD-adr025-implementation-pending** | |
| `UserPiiErased` | TENANT_TARGETED | user's `primary_org_id` (recorded BEFORE NULL-out) | `user_id` | `{user_id, erased_columns, erasure_token_hash}` | **TBD-adr025-implementation-pending** | |
| `CrossTenantReportAccessed` | GLOBAL_ADMIN_ONLY | NULL | `report_id` | `{report_type, parameter_hash}` | **TBD-adr025-implementation-pending** | |

### Retrofit candidates (42)

| event_type | visibility_scope | target_org_resolution | target_resource_id | details_shape | mapper_kind | sprint_landed |
|------------|------------------|-----------------------|--------------------|---------------|-------------|---------------|
| `OrganizationCreated` | TENANT_TARGETED | the org itself | NULL | `{org_id, org_name, parent_org_id?}` | TBD | |
| `OrganizationUpdated` | TENANT_TARGETED | the org itself | NULL | `{org_id, before, after}` | TBD | |
| `UserCreated` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, primary_org_id, roles}` | TBD | |
| `UserUpdated` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, before, after}` | TBD | |
| `RoleAssignmentGranted` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, role, scope}` | TBD | |
| `RoleAssignmentRevoked` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, role, scope}` | TBD | |
| `AgreementConfigCreated` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{config_id, agreement_code, ok_version, status}` | TBD | |
| `AgreementConfigUpdated` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{config_id, before, after}` | TBD | |
| `AgreementConfigPublished` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{config_id, supersedes?}` | TBD | |
| `AgreementConfigArchived` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{config_id, archived_at}` | TBD | |
| `AgreementConfigCloned` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{new_config_id, source_config_id}` | TBD | |
| `LocalConfigurationChanged` | TENANT_TARGETED | the local config's `org_id` | NULL | `{org_id, key, before, after}` | TBD | |
| `PositionOverrideCreated` | GLOBAL_TENANT_VISIBLE | NULL | `override_id` | `{override_id, position_id, ...}` | TBD | |
| `PositionOverrideUpdated` | GLOBAL_TENANT_VISIBLE | NULL | `override_id` | `{override_id, before, after}` | TBD | |
| `PositionOverrideActivated` | GLOBAL_TENANT_VISIBLE | NULL | `override_id` | `{override_id, activated_at}` | TBD | |
| `PositionOverrideDeactivated` | GLOBAL_TENANT_VISIBLE | NULL | `override_id` | `{override_id, deactivated_at}` | TBD | |
| `WageTypeMappingCreated` | GLOBAL_TENANT_VISIBLE | NULL | `mapping_id` | `{mapping_id, ...natural_key}` | TBD | |
| `WageTypeMappingUpdated` | GLOBAL_TENANT_VISIBLE | NULL | `mapping_id` | `{mapping_id, before, after}` | TBD | |
| `WageTypeMappingDeleted` | GLOBAL_TENANT_VISIBLE | NULL | `mapping_id` | `{mapping_id, deleted_at}` | TBD | |
| `WageTypeMappingSuperseded` | GLOBAL_TENANT_VISIBLE | NULL | `mapping_id` | `{previous_id, new_id, effective_from}` | TBD | |
| `EntitlementConfigSeeded` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{config_id, type, ...natural_key}` | TBD | |
| `EntitlementConfigCreated` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{config_id, type, ...}` | TBD | |
| `EntitlementConfigSuperseded` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{previous_id, new_id, effective_from}` | TBD | |
| `EntitlementConfigSoftDeleted` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{deleted_at}` | TBD | |
| `LocalAgreementProfileChanged` | TENANT_TARGETED | the profile's `org_id` | `profile_id` | `{org_id, profile_id, before, after}` | TBD | |
| `PeriodSubmitted` | TENANT_TARGETED | `employee → users.primary_org_id` | `period_id` | `{employee_id, period_id, period_start, period_end}` | TBD | |
| `PeriodApproved` | TENANT_TARGETED | `employee → users.primary_org_id` | `period_id` | `{employee_id, period_id, approved_by}` | TBD | |
| `PeriodRejected` | TENANT_TARGETED | `employee → users.primary_org_id` | `period_id` | `{employee_id, period_id, rejection_reason}` | TBD | |
| `PeriodEmployeeApproved` | TENANT_TARGETED | `employee → users.primary_org_id` | `period_id` | `{employee_id, period_id}` | TBD | |
| `PeriodReopened` | TENANT_TARGETED | `employee → users.primary_org_id` | `period_id` | `{employee_id, period_id, reopened_by}` | TBD | |
| `OvertimePreApprovalCreated` | TENANT_TARGETED | `employee → users.primary_org_id` | `preapproval_id` | `{employee_id, hours, period}` | TBD | |
| `OvertimePreApprovalApproved` | TENANT_TARGETED | `employee → users.primary_org_id` | `preapproval_id` | `{preapproval_id, approved_by, approved_at}` | TBD | |
| `OvertimePreApprovalRejected` | TENANT_TARGETED | `employee → users.primary_org_id` | `preapproval_id` | `{preapproval_id, rejected_by, rejection_reason}` | TBD | |
| `RetroactiveCorrectionRequested` | TENANT_TARGETED | `employee → users.primary_org_id` | `correction_id` | `{employee_id, period, correction_type, ...}` | **TBD-cross-process-deferred** | |
| `PayrollExportGenerated` | TENANT_TARGETED | `employee → users.primary_org_id` (one row per (export, employee) OR `target_org_id = NULL` + scope=GLOBAL_TENANT_VISIBLE — Sub-Sprint 2 picks per ADR-026 D3 L180 deferred user-decision) | export request id | `{export_id, period, employee_count, file_format}` | **TBD-defined-but-unemitted** | |
| `UserAgreementCodeChanged` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, old_agreement_code, new_agreement_code, effective_from}` | TBD | |
| `UserAgreementCodeSeeded` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, agreement_code, ok_version, effective_from}` | TBD | |
| `UserAgreementCodeSuperseded` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{previous_id, new_id, effective_from}` | TBD | |
| `EmployeeProfileCreated` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employee_id, weekly_norm_hours, part_time_fraction, position}` | TBD | |
| `EmployeeProfileUpdated` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employee_id, before, after}` | TBD | |
| `EmployeeProfileSuperseded` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{previous_id, new_id, effective_from}` | TBD | |
| `EmployeeProfileSoftDeleted` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{deleted_at}` | TBD | |

**Total**: 53 rows (11 new + 42 retrofit). Matches ADR-026 D3 inventory count.

## Known inventory gaps (Sub-Sprint 2 to resolve)

- **`UserAgreementCodeSoftDeleted`** — ADR-026 D3 L181 enumerates only 3 UserAgreementCode events but `MEMORY S34` documents 4 (Changed + Seeded + Superseded + SoftDeleted). Catalog reflects ADR-026 L181 fidelity (3 rows) — Sub-Sprint 2 verifies completeness against actual EventSerializer typeofs + either adds the row OR confirms intentional omission.
- **`OrganizationDeleted`** / **`UserDeleted`** — If org / user deletion paths exist or are added, they belong here. Verify against EventSerializer typeofs at Sub-Sprint 2 mapper authorship.
- **4 ADR-025 events not yet implemented**: `InstitutionProvisioned`, `InstitutionDataExported`, `UserPiiErased`, `CrossTenantReportAccessed` are listed under "New events from S38 ADRs (11)" but NOT registered in `EventSerializer.cs` — ADR-025 was authored DESIGN-ONLY at S38 (no code emitted). Sub-Sprint 2 either (a) implements the endpoints that emit these events alongside their mappers OR (b) explicitly defers them to a separate sprint (catalog rows then carry `mapper_kind: TBD-adr025-implementation-pending`). Step 7a cycle 1 Reviewer W1 absorption — Phase E Test #1 (Sub-Sprint 3) will assert catalog ↔ EventSerializer parity; today, these 4 rows would fail the assertion.

## Explicitly NOT audit-relevant (per ADR-026 D3 L184-194)

For reference (these do NOT get rows in audit_projection):

- `TimeEntryRegistered`, `AbsenceRegistered` (high-volume; visible via S27 projections)
- `TimerCheckedIn`, `TimerCheckedOut` (operational)
- `NormCheckCompleted`, `FlexBalanceUpdated`, `SupplementCalculated`, `OvertimeCalculated`, `PeriodCalculationCompleted`, `OvertimeCompensationApplied` (rule-engine internals)
- `RestPeriodViolationDetected`, `CompensatoryRestGranted` (compliance flags; future revisit)
- `IntegrationDeliveryTracked` (outbox telemetry)
- `SegmentManifestCreated` (PCS internals)
- `OvertimeBalanceAdjusted`, `EntitlementBalanceAdjusted` (balance projections; future revisit)

## Validation invariants (Phase E Test #1 in Sub-Sprint 3)

Test #1 will assert these invariants programmatically:

1. **Catalog ↔ EventSerializer**: every catalog `event_type` resolves to a registered EventSerializer typeof; no catalog row references a non-existent type.
2. **Catalog ↔ DI**: every catalog row with `mapper_kind` ∈ {`interface`, `endpoint-direct`} has a corresponding `IAuditProjectionMapper<TEvent>` DI registration. `TBD-*` rows are exempt (deferred adjudication).
3. **Catalog ↔ visibility CHECK**: every `visibility_scope` cell is one of the 3 allowed enum values.
4. **Catalog ↔ target_org rule**: every `TENANT_TARGETED` row has a non-`NULL` `target_org_resolution`; every `GLOBAL_*` row has `target_org_resolution = NULL`.

Sub-Sprint 3 ships the parser + the four assertions. Sub-Sprint 1 ships the locked structure.
