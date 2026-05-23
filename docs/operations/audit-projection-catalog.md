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

## Known TBD-with-suffix markers (Step 0b cycle 1 absorption)

**Two known seams blocked from `interface` / `endpoint-direct` resolution at Sub-Sprint 1 close**:

1. **`TBD-payroll-dispatch-seam`** — `RetroactiveCorrectionRequested` is emitted from `Payroll.Integrations/Services/RetroactiveCorrectionService.cs:222` via `IEventStore.AppendAsync` (self-managed connection; fire-and-forget try/catch), NOT via `IOutboxEnqueue.EnqueueAndReturnIdAsync(conn, tx, ...)` which ADR-026 D2 dispatch requires. Sub-Sprint 2 resolves via EITHER (i) rewrite `RetroactiveCorrectionService.cs:222` to (conn, tx) + IOutboxEnqueue pattern (gives Payroll service a `DbConnectionFactory`), OR (ii) narrow ADR-026 errata explicitly excluding this single event from D3 (acknowledges it stays invisible to audit until cross-process dispatch is solved holistically). NO backfill-only path — async backfill violates ADR-026 D2's sync-in-tx requirement (Step 0b cycle 1 Codex BLOCKER B1 absorption).

2. **`TBD-l194-reconciliation`** — ADR-026 has an internal contradiction: D3 L182 includes `EmployeeProfileCreated/Updated/Superseded/SoftDeleted` as TENANT_TARGETED retrofit candidates, but D3 L194 NOT-audit-relevant prose says `EmploymentProfile*` (fictive name) is excluded as "versioned config; pre-existing audit captured by upstream user_agreement_code lifecycle events." Reading L194 charitably as referring to `EmployeeProfile*` produces a contradiction with L182. Sub-Sprint 2 resolves via EITHER (a) commit ADR-026 errata removing the 4 EmployeeProfile* rows from D3 INCLUDE list (L194's intent wins; user_agreement_code lifecycle events cover the equivalent visibility), OR (b) commit ADR-026 errata removing L194 reference (L182's intent wins; EmployeeProfile* events get their own mappers). NOT silently "corrected as typo" per Step 0b cycle 1 Reviewer BLOCKER absorption.

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
| `InstitutionProvisioned` | TENANT_TARGETED | new institution's `org_id` | `institution_id` | `{org_id, legal_name, subscription_tier, onboarded_by}` | TBD | |
| `InstitutionDataExported` | TENANT_TARGETED | exported institution's `org_id` | export request id | `{org_id, requesting_actor_id, export_size_bytes}` | TBD | |
| `UserPiiErased` | TENANT_TARGETED | user's `primary_org_id` (recorded BEFORE NULL-out) | `user_id` | `{user_id, erased_columns, erasure_token_hash}` | TBD | |
| `CrossTenantReportAccessed` | GLOBAL_ADMIN_ONLY | NULL | `report_id` | `{report_type, parameter_hash}` | TBD | |

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
| `RetroactiveCorrectionRequested` | TENANT_TARGETED | `employee → users.primary_org_id` | `correction_id` | `{employee_id, period, correction_type, ...}` | **TBD-payroll-dispatch-seam** | |
| `PayrollExportGenerated` | TENANT_TARGETED | `employee → users.primary_org_id` (one row per (export, employee) OR `target_org_id = NULL` + scope=GLOBAL_TENANT_VISIBLE — Sub-Sprint 2 picks per ADR-026 D3 L180 deferred user-decision) | export request id | `{export_id, period, employee_count, file_format}` | TBD | |
| `UserAgreementCodeChanged` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, old_agreement_code, new_agreement_code, effective_from}` | TBD | |
| `UserAgreementCodeSeeded` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, agreement_code, ok_version, effective_from}` | TBD | |
| `UserAgreementCodeSuperseded` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{previous_id, new_id, effective_from}` | TBD | |
| `EmployeeProfileCreated` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employee_id, weekly_norm_hours, part_time_fraction, position}` | **TBD-l194-reconciliation** | |
| `EmployeeProfileUpdated` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employee_id, before, after}` | **TBD-l194-reconciliation** | |
| `EmployeeProfileSuperseded` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{previous_id, new_id, effective_from}` | **TBD-l194-reconciliation** | |
| `EmployeeProfileSoftDeleted` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{deleted_at}` | **TBD-l194-reconciliation** | |

**Total**: 53 rows (11 new + 42 retrofit). Matches ADR-026 D3 inventory count.

## Known inventory gaps (Sub-Sprint 2 to resolve)

- **`UserAgreementCodeSoftDeleted`** — ADR-026 D3 L181 enumerates only 3 UserAgreementCode events but `MEMORY S34` documents 4 (Changed + Seeded + Superseded + SoftDeleted). Catalog reflects ADR-026 L181 fidelity (3 rows) — Sub-Sprint 2 verifies completeness against actual EventSerializer typeofs + either adds the row OR confirms intentional omission.
- **`OrganizationDeleted`** / **`UserDeleted`** — If org / user deletion paths exist or are added, they belong here. Verify against EventSerializer typeofs at Sub-Sprint 2 mapper authorship.

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
