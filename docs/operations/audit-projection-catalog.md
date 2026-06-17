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
| `RoleConfigOverrideCreated` | GLOBAL_TENANT_VISIBLE | NULL | `(employment_category:agreement_code:ok_version)` | `{overrideId, employmentCategory, agreementCode, okVersion, effectiveFrom}` | interface (mapper-only, no emit site) | S44c |
| `RoleConfigOverrideUpdated` | GLOBAL_TENANT_VISIBLE | NULL | `override_id` | `{overrideId, versionBefore, versionAfter}` | interface (mapper-only) | S44c |
| `RoleConfigOverrideSuperseded` | GLOBAL_TENANT_VISIBLE | NULL | `predecessor_override_id` | `{predecessorOverrideId, successorOverrideId, effectiveFrom, ...}` | interface (mapper-only) | S44c |
| `RoleConfigOverrideSoftDeleted` | GLOBAL_TENANT_VISIBLE | NULL | `override_id` | `{overrideId, effectiveTo, employmentCategory, ...}` | interface (mapper-only) | S44c |
| `MerarbejdeDiscretionary` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employeeId, date, merarbejdeHours, employmentCategory}` | interface (mapper-only) | S44c |
| `OvertimeNecessityAcknowledged` | TENANT_TARGETED | `employee → users.primary_org_id` | `preapproval_id` | `{preApprovalId, necessityReason, acknowledgedEntryCount}` | interface (mapper-only) | S44c |
| `ConfigBugCorrected` | GLOBAL_TENANT_VISIBLE | NULL | `config_key` | `{configSurface, configKey, fromValue, toValue, source, classifier, action}` | interface (mapper-only) | S44c |
| `InstitutionProvisioned` | TENANT_TARGETED | new institution's `org_id` | `institution_id` | `{org_id, legal_name, subscription_tier, onboarded_by}` | **TBD-adr025-implementation-pending** | |
| `InstitutionDataExported` | TENANT_TARGETED | exported institution's `org_id` | export request id | `{org_id, requesting_actor_id, export_size_bytes}` | **TBD-adr025-implementation-pending** | |
| `UserPiiErased` | TENANT_TARGETED | user's `primary_org_id` (recorded BEFORE NULL-out) | `user_id` | `{user_id, erased_columns, erasure_token_hash}` | **TBD-adr025-implementation-pending** | |
| `CrossTenantReportAccessed` | GLOBAL_ADMIN_ONLY | NULL | `report_id` | `{report_type, parameter_hash}` | **TBD-adr025-implementation-pending** | |

### Retrofit candidates (42)

| event_type | visibility_scope | target_org_resolution | target_resource_id | details_shape | mapper_kind | sprint_landed |
|------------|------------------|-----------------------|--------------------|---------------|-------------|---------------|
| `OrganizationCreated` | TENANT_TARGETED | the org itself | NULL | `{org_id, org_name, parent_org_id?}` | interface | S44 `bba76aa` |
| `OrganizationUpdated` | TENANT_TARGETED | the org itself | NULL | `{org_id, before, after}` | interface | S44 `bba76aa` |
| `UserCreated` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, primary_org_id, roles}` | interface | S44 `bba76aa` |
| `UserUpdated` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, before, after}` | interface | S44 `bba76aa` |
| `RoleAssignmentGranted` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, role, scope}` | interface | S44 `bba76aa` |
| `RoleAssignmentRevoked` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, role, scope}` | interface | S44 `bba76aa` |
| `AgreementConfigCreated` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{config_id, agreement_code, ok_version}` | interface | S44b |
| `AgreementConfigUpdated` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{config_id, agreement_code, ok_version}` | interface | S44b |
| `AgreementConfigPublished` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{config_id, agreement_code, ok_version, archived_config_id?}` | interface | S44b |
| `AgreementConfigArchived` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{config_id, agreement_code, ok_version}` | interface | S44b |
| `AgreementConfigCloned` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{config_id, source_config_id, agreement_code, ok_version}` | interface | S44b |
| `LocalConfigurationChanged` | TENANT_TARGETED | the local config's `org_id` | `config_id` | `{configId, orgId, configArea, configKey, configValue, previousValue}` | interface (mapper-only, no emit site — legacy pre-S21) | S44c |
| `PositionOverrideCreated` | GLOBAL_TENANT_VISIBLE | NULL | `override_id` | `{overrideId, agreementCode, okVersion, positionCode}` | interface | S44c |
| `PositionOverrideUpdated` | GLOBAL_TENANT_VISIBLE | NULL | `override_id` | `{overrideId, agreementCode, okVersion, positionCode}` | interface | S44c |
| `PositionOverrideActivated` | GLOBAL_TENANT_VISIBLE | NULL | `override_id` | `{overrideId, agreementCode, okVersion, positionCode}` | interface | S44c |
| `PositionOverrideDeactivated` | GLOBAL_TENANT_VISIBLE | NULL | `override_id` | `{overrideId, agreementCode, okVersion, positionCode}` | interface | S44c |
| `WageTypeMappingCreated` | GLOBAL_TENANT_VISIBLE | NULL | `(timeType:agreementCode:okVersion:position)` | `{timeType, wageType, okVersion, agreementCode, position}` | interface | S44c |
| `WageTypeMappingUpdated` | GLOBAL_TENANT_VISIBLE | NULL | `(timeType:agreementCode:okVersion:position)` | `{timeType, wageType, okVersion, agreementCode, position}` | interface | S44c |
| `WageTypeMappingDeleted` | GLOBAL_TENANT_VISIBLE | NULL | `(timeType:agreementCode:okVersion:position)` | `{timeType, okVersion, agreementCode, position}` | interface | S44c |
| `WageTypeMappingSuperseded` | GLOBAL_TENANT_VISIBLE | NULL | `(timeType:agreementCode:okVersion:position)` | `{timeType, wageType, okVersion, agreementCode, position}` | interface | S44c |
| `EntitlementConfigSeeded` | GLOBAL_TENANT_VISIBLE | NULL | `(agreementCode:okVersion)` | `{agreementCode, okVersion, configCount}` | interface (mapper-only, no emit site) | S44c |
| `EntitlementConfigCreated` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{configId, entitlementType, agreementCode, okVersion, effectiveFrom, annualQuota, accrualModel, resetMonth, fullDayOnly}` | interface | S44c (·`fullDayOnly` S73) |
| `EntitlementConfigSuperseded` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{configId, entitlementType, supersededByConfigId, effectiveFrom, fullDayOnly}` | interface | S44c (·`fullDayOnly` S73) |
| `EntitlementConfigSoftDeleted` | GLOBAL_TENANT_VISIBLE | NULL | `config_id` | `{configId, entitlementType, agreementCode, okVersion}` | interface | S44c |
| `LocalAgreementProfileChanged` | TENANT_TARGETED | the profile's `org_id` | `profile_id` | `{profileId, orgId, agreementCode, okVersion, effectiveFrom}` | interface | S44c |
| `PeriodSubmitted` | TENANT_TARGETED | `employee → users.primary_org_id` | `period_id` | `{period_id, employee_id, period_start, period_end, period_type}` | interface | S44b |
| `PeriodApproved` | TENANT_TARGETED | `employee → users.primary_org_id` | `period_id` | `{period_id, employee_id, period_start, period_end, approved_by}` | interface | S44b |
| `PeriodRejected` | TENANT_TARGETED | `employee → users.primary_org_id` | `period_id` | `{period_id, employee_id, period_start, period_end, rejected_by, rejection_reason}` | interface | S44b |
| `PeriodEmployeeApproved` | TENANT_TARGETED | `employee → users.primary_org_id` | `period_id` | `{period_id, employee_id, period_start, period_end}` | interface | S44b |
| `PeriodReopened` | TENANT_TARGETED | `employee → users.primary_org_id` | `period_id` | `{period_id, employee_id, period_start, period_end, reason}` | interface | S44b |
| `OvertimePreApprovalCreated` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employee_id, period_start, period_end, max_hours, status}` | interface | S44b |
| `OvertimePreApprovalApproved` | TENANT_TARGETED | `employee → users.primary_org_id` | `preapproval_id` | `{preapproval_id, employee_id, approved_by, reason}` | interface | S44b |
| `OvertimePreApprovalRejected` | TENANT_TARGETED | `employee → users.primary_org_id` | `preapproval_id` | `{preapproval_id, employee_id, rejected_by, reason}` | interface | S44b |
| `RetroactiveCorrectionRequested` | TENANT_TARGETED | `employee → users.primary_org_id` (via EmploymentProfile.OrgId) | `employee_id` | `{employeeId, originalPeriodStart, originalPeriodEnd, agreementCode, okVersion, reason, correctedByActorId, correctionLineCount, totalDifferenceHours, manifestId}` | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S45 |
| `PayrollExportGenerated` | TENANT_TARGETED | `employee → users.primary_org_id` (one row per (export, employee) OR `target_org_id = NULL` + scope=GLOBAL_TENANT_VISIBLE — Sub-Sprint 2 picks per ADR-026 D3 L180 deferred user-decision) | export request id | `{export_id, period, employee_count, file_format}` | **TBD-defined-but-unemitted** | |
| `UserAgreementCodeChanged` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, old_agreement_code, new_agreement_code, effective_from}` | interface | S44b |
| `UserAgreementCodeSeeded` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{user_id, agreement_code, effective_from, row_version}` | interface | S44b |
| `UserAgreementCodeSuperseded` | TENANT_TARGETED | `user → users.primary_org_id` | `user_id` | `{predecessor_assignment_id, new_assignment_id, user_id, ...effective_dates, old/new_agreement_code, version_before/after}` | interface | S44b |
| `EmployeeProfileCreated` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{profileId, employeeId, weeklyNormHours, partTimeFraction, position, effectiveFrom}` | interface | S44c |
| `EmployeeProfileUpdated` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{profileId, employeeId, weeklyNormHours, partTimeFraction, position, versionBefore, versionAfter}` | interface | S44c |
| `EmployeeProfileSuperseded` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{predecessorProfileId, newProfileId, employeeId, predecessorEffectiveFrom, newEffectiveFrom, ...}` | interface | S44c |
| `EmployeeProfileSoftDeleted` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{profileId, employeeId, effectiveTo}` | interface | S44c |
| `EmployeeEntitlementEligibilitySet` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employeeId, entitlementType, eligible, effectiveFrom}` | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S59 |
| `EntitlementBalanceRevalued` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employeeId, entitlementType, entitlementYear, usedDelta, affectedAbsenceCount, replacements, triggeringProfileEventId}` | interface | S66 |
| `VacationCarryoverExecuted` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employeeId, entitlementType, entitlementYear, sequence, transferDays, kind, paragraph}` | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S68 |
| `VacationAutoPaidOut` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employeeId, entitlementType, entitlementYear, sequence, payoutDays, kind, paragraph}` | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S68 |
| `VacationForfeitedToFeriefond` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employeeId, entitlementType, entitlementYear, sequence, forfeitDays, kind, paragraph}` (S70 OVERLOAD: also emitted by FORFEIT on a leaver deferred-disposition row — SPRINT-70 R4 — where forfeitDays is an UNPARTITIONED full disposable, NOT a computed §34 remittance; discriminate via the snapshot's `DeferredDisposition` marker) | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S68 |
| `SettlementManualReviewFlagged` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employeeId, entitlementType, entitlementYear, sequence, flaggedDays, disposition}` | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S68 |
| `SaerligeFeriedagePaidOut` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{kind, paragraph (§15/§17), employeeId, entitlementType, entitlementYear, sequence, payoutDays, okVersion}` (the særlige-feriedage §15 stk.2/§17 godtgørelse — the unused remainder of a closed SPECIAL_HOLIDAY accrual year, settled SETTLED at the 30-Apr-Y+2 boundary as a day-count payout; NEVER §34-forfeited; the SLS_TBD_* line is 8003, this event is the settlement-fact emit — SPRINT-80 R4/R8; graduated from define-only to emitted in S80) | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S80 |
| `FeriehindringTransferred` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{kind, paragraph (§22), disposition (FERIEHINDRING), employeeId, entitlementType, entitlementYear, sequence, transferDays, feriehindringReason, okVersion}` (the §22 impediment RESCUE from the §34 forfeiture bucket into next year's carryover_in — emitted by the FERIEHINDRING resolve disposition; BALANCE-ONLY, NO payroll line; the residual §34 reuses the `VacationForfeitedToFeriefond` mapper, SPRINT-79 R1/R3) | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S79 |
| `EmployeeEmploymentEndDateSet` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employeeId, oldEndDate, newEndDate, oldIsActive, newIsActive, versionBefore, versionAfter}` | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S70 |
| `EmployeeEndDateDeactivationApplied` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employeeId, endDate, oldIsActive, newIsActive, versionBefore, versionAfter}` (system actor — the Step-A poller flip, SPRINT-70 R2) | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S70 |
| `TerminationSettled` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{employeeId, entitlementType, entitlementYear, sequence, payoutDays, modregningDays, unearnedAdvanceDays, carryoverIn, okVersion, paragraph}` (§26+§7; **emitted, settlement-fact only** — the §26 LINE is driven by `TerminationPayoutRequested` per the SPRINT-71 event-model amendment) | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S70 |
| `TerminationPayoutRequested` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{kind, paragraph (§26), employeeId, entitlementType, entitlementYear, settlementSequence, requestDate, evidenceNote, crystallizedDays, settlementBoundaryDate}` (the §26 anmodning fact — DRIVES the staged `SLS_TBD_S26` line; SPRINT-71 R6) | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S71 |
| `TerminationClaimWaived` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{kind, paragraph (§7), disposition (WAIVED), employeeId, entitlementType, entitlementYear, settlementSequence, waivedDays}` (the waiver resolution — NO line stages; the §7 MODREGNING sibling is PARKED pending the SLS dialogue, SPRINT-71 gate (i)) | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S71 |
| `SettlementReversed` | TENANT_TARGETED | `employee → users.primary_org_id` | `employee_id` | `{kind, employeeId, entitlementType, entitlementYear, settlementSequence, reversalKind (BARE/SUPERSEDED), successorSequence, trigger, transferDays, payoutDays, forfeitDays, crystallizedDays, claimDispositionDays, okVersion}` (graduated define-only → emitted in S71; compensation targets derive consumer-side per SPRINT-71 R9 — the payload carries NO staged-line references) | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S71 |
| `ManagerVikarCreated` | TENANT_TARGETED | event `tree_root_org_id` (the styrelse tree root, carried in the payload) | `vikar_id` | `{vikarId, absentApproverId, vikarUserId, untilDate, reason, treeRootOrgId, rowVersion}` (the approver-owned vikar create — replaces the per-report SELF_DELEGATION fan-out; ADR-027 Phase 5 / SPRINT-74 R4) | interface (cross-process — mapper in Infrastructure, not Backend.Api; the DelegationExpiryService also emits) | S74 |
| `ManagerVikarEnded` | TENANT_TARGETED | event `tree_root_org_id` (the styrelse tree root, carried in the payload) | `vikar_id` | `{vikarId, absentApproverId, vikarUserId, untilDate, reason, treeRootOrgId, effectiveTo, endReason (REVOKED/EXPIRED/APPROVER_REMOVED), rowVersion}` (vikar close — REVOKED via DELETE, EXPIRED via DelegationExpiryService, or APPROVER_REMOVED via the R10 delete-with-reassignment; SPRINT-74 R4/R4a/R10) | interface (cross-process — mapper in Infrastructure, not Backend.Api) | S74 |

**Total**: 68 rows (54 through S66 + 4 S68 vacation-settlement mappers per ADR-033 slice 1a + 3 S70 termination-foundation mappers per ADR-033 slice 3a + 3 S71 slice-3b mappers — `SettlementReversed` graduated from define-only to emitted in S71 — + 2 S74 manager-vikar mappers per ADR-027 Phase 5 + 1 S79 slice-4 mapper (`FeriehindringTransferred` — graduated from define-only to emitted by the §22 FERIEHINDRING resolve disposition) + 1 S80 slice-2 mapper (`SaerligeFeriedagePaidOut` — graduated from define-only to emitted by the SPECIAL_HOLIDAY §15 stk.2/§17 godtgørelse close); the 1 remaining DEFINE-ONLY settlement event — `FeriehindringPaidOut` (§25) — is EventSerializer-registered for replay but has NO mapper/catalog row until its automation slice; the PARKED §7 `TerminationModregningApplied` is NOT defined at all — its payload shape awaits the SLS dialogue, SPRINT-71 gate (i)).

## Catalog closure status (S44c)

**48 of 53 rows have `mapper_kind = interface`** — all mappers shipped across S44/S44b/S44c/S45.

**5 rows remain deferred with TBD-* markers:**
- 1× `TBD-defined-but-unemitted` (PayrollExportGenerated — intentionally deferred; vestigial S22 event class in EventSerializer with zero production emit sites; harmless for backward-compat; if payroll-export audit trail is needed, gets its own future sprint with emit site + mapper)
- 4× `TBD-adr025-implementation-pending` (InstitutionProvisioned, InstitutionDataExported, UserPiiErased, CrossTenantReportAccessed)

**9 mapper-only rows** (mapper + DI exist, no endpoint emit site): LocalConfigurationChanged (legacy pre-S21), EntitlementConfigSeeded (never emitted), RoleConfigOverride×4 (ADR-024 suspended), MerarbejdeDiscretionary, OvertimeNecessityAcknowledged, ConfigBugCorrected. Endpoint cutovers ship with feature sprints.

**Resolved gaps:**
- `UserAgreementCodeSoftDeleted` — RESOLVED at S44b (does not exist).
- `OrganizationDeleted` / `UserDeleted` — no event classes exist.

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
