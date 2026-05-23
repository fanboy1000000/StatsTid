# Audit Projection Caller Census

> **Sprint**: S44 / TASK-4401 (ADR-026 Sub-Sprint 2)
> **Purpose**: Enumerate every audit-relevant event's emit site(s) with atomic-outbox-shape annotation, per the `feedback_cross_process_caller_census_required.md` discipline (commissioned at S42a discipline-rollback). Companion to `audit-projection-catalog.md`; the catalog says what gets a projection row, the census says where the row is written from.

## Why this exists

ADR-026 D2 requires sync-in-tx projection write: every audit-relevant event must be emitted via `(conn, tx)` + `IOutboxEnqueue.EnqueueAndReturnIdAsync(...)` so the projection row commits atomically with the source event. The census surfaces:

1. **Cross-process emitters** — events emitted from a service other than Backend.Api (Payroll.Integrations, Orchestrator). These can't ride a `(conn, tx)` shape today; each needs an explicit adjudication.
2. **Multi-emit endpoints** — endpoints that emit multiple audit-relevant events in the same atomic tx. Each emit site needs its own `(conn, tx)` + outbox-id-capture + mapper invocation.
3. **Defined-but-unemitted events** — event classes registered in EventSerializer but with no production emit site (vestigial classes that need decommissioning OR a deferred-feature flag).
4. **In-process emit shape** — verifies each in-scope emitter uses the atomic-outbox-compatible call pattern (per S26+S31+S33+S34 cutovers).

## Method

Grep patterns (run from `/c/StatsTid`):
- `_eventStore\.AppendAsync|IEventStore.AppendAsync` over `src/Integrations/**`, `src/Orchestrator/**`, `src/Backend/**`
- `outbox\.EnqueueAsync|outbox\.EnqueueAndReturnIdAsync` over `src/Backend/**`
- `new <EventClassName>` per audit-relevant event in the catalog (cross-checked against emit sites)

Results consolidated 2026-05-23 (post-S43 close commit `f62b8cb`).

## Cross-process emit sites

| Site | Event | Audit-relevant per ADR-026 D3? | Atomic-outbox shape? | S44 disposition |
|------|-------|-------------------------------|----------------------|-----------------|
| `src/Integrations/StatsTid.Integrations.Payroll/Services/RetroactiveCorrectionService.cs:222` | `RetroactiveCorrectionRequested` | YES (L179) | NO — `IEventStore.AppendAsync` (self-managed connection; no DbConnectionFactory in Payroll) | **Deferred** via `TBD-cross-process-deferred` catalog marker; dedicated S44-cross-process sprint owns the resolution (Payroll DbConnectionFactory introduction OR architectural carve-out). |
| `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:974` | `SegmentManifestCreated` | NO (L192 — PCS internals) | n/a | No action; not in audit_projection scope. |
| `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:1131` | `PeriodCalculationCompleted` (legacy) | NO (L189 — rule-engine internals) | n/a | No action; not in audit_projection scope. |
| `src/Orchestrator/**` | — | (no `.AppendAsync` sites found) | n/a | Clean. |

**Summary**: 1 audit-relevant cross-process emitter (RetroactiveCorrectionRequested) — explicitly deferred. The discipline assumption that "cross-process audit emission is rare" holds; ADR-024's cross-process complexity does not generalize across audit-emission paths.

## Defined-but-unemitted events

| Event class | Defined in | Registered in EventSerializer | Production emit sites | Disposition |
|-------------|------------|-------------------------------|------------------------|-------------|
| `PayrollExportGenerated` | `src/SharedKernel/StatsTid.SharedKernel/Events/PayrollExportGenerated.cs` | YES (`EventSerializer.cs:20`) | **ZERO** (grep `new PayrollExportGenerated\|PayrollExportGenerated\s*\{\|PayrollExportGenerated\(` returns no matches in `src/`) | **Deferred** via `TBD-defined-but-unemitted` catalog marker; S44f decides between (a) add emit code + mapper OR (b) remove the vestigial class. Likely S22 leftover from the payroll-trace-event plan. |

**Summary**: 1 audit-relevant defined-but-unemitted event. Phase E Test #1 (S45+) would flag this; the marker makes the gap explicit pre-test.

## In-scope S44 emit sites (Org/User/RoleAssignment exemplar family)

All 6 emit sites are in `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs`, all use `(conn, tx)` atomic-outbox shape from prior cutovers (S26 + S31 + S33 + S34), but all currently call `EnqueueAsync` (void return) instead of `EnqueueAndReturnIdAsync` (returns `long outboxId`). S44 TASK-4413 converts all 6.

| Site | Event | Endpoint | Multi-emit? | S44 cutover |
|------|-------|----------|-------------|-------------|
| `AdminEndpoints.cs:154` | `OrganizationCreated` | POST `/api/admin/organizations` | No | Convert to `EnqueueAndReturnIdAsync` + audit insert |
| `AdminEndpoints.cs:237` | `OrganizationUpdated` | PUT `/api/admin/organizations/{orgId}` | No | Convert to `EnqueueAndReturnIdAsync` + audit insert |
| `AdminEndpoints.cs:587` | `UserCreated` | POST `/api/admin/users` | **YES** — also emits EmployeeProfileCreated@L611 + UserAgreementCodeSeeded@L622+L632 | Convert ONLY the UserCreated enqueue to `EnqueueAndReturnIdAsync` + audit insert; other 2 emits stay as-is (S44b/c reopens for those mappers). The audit_projection row for UserCreated rides the same tx as all 3 enqueues. |
| `AdminEndpoints.cs:1033` | `UserUpdated` | PUT `/api/admin/users/{userId}` | **YES** — also emits UserAgreementCodeChanged@L1163+L1173 + UserAgreementCodeSuperseded@L1186+L1202 (Case C cross-day) | Convert ONLY the UserUpdated enqueue + audit insert; other 2 emit sites stay as-is. |
| `AdminEndpoints.cs:1429` | `RoleAssignmentGranted` | POST `/api/admin/users/{userId}/roles` | No | Convert + audit insert |
| `AdminEndpoints.cs:1542` | `RoleAssignmentRevoked` | DELETE `/api/admin/users/{userId}/roles/{role}` | No | Convert + audit insert |

**Implication for forced-rollback D-tests (TASK-4414)**: the two multi-emit endpoints (POST/PUT /users) allow throwing on the SECOND or THIRD enqueue — which fires AFTER the UserCreated/UserUpdated audit insert has already written. This proves post-audit-insert tx rollback. The four single-emit endpoints (orgs + roles) use the S27 TimeProjectionAtomicTests pattern: throw on the only EnqueueAndReturnIdAsync → tx aborts pre-audit-insert → no audit row lands.

## Deferred batches (S44b/c/f)

The remaining ~47 audit-relevant events (53 catalog rows − 6 S44 in-scope) are batched for future sub-sprints. Each future sub-sprint's refinement re-runs the caller-census step to capture any newly-added emit sites.

| Batch | Events | Target sub-sprint | Notes |
|-------|--------|-------------------|-------|
| Agreement config | `AgreementConfigCreated/Updated/Published/Archived/Cloned` (5) | S44b | Emit via AgreementConfigRepository.PublishAsync internal calls; verify (conn, tx) shape at refinement |
| Local config | `LocalConfigurationChanged` (1) | S44b | Emit from ConfigEndpoints S22 atomic exemplar |
| Position override | `PositionOverrideCreated/Updated/Activated/Deactivated` (4) | S44b | Emit from S25 atomic CRUD |
| Wage type mapping | `WageTypeMappingCreated/Updated/Deleted/Superseded` (4) | S44b | Emit from S25 + S29 versioned-history pattern |
| Entitlement config | `EntitlementConfigSeeded/Created/Superseded/SoftDeleted` (4) | S44b | Emit from S30 versioned-history pattern |
| Period workflow | `PeriodSubmitted/Approved/Rejected/EmployeeApproved/Reopened` (5) | S44b | Emit from ApprovalEndpoints |
| Overtime | `OvertimePreApprovalCreated/Approved/Rejected` (3) | S44b | Emit from OvertimeEndpoints S17 + S26 |
| User agreement code | `UserAgreementCodeChanged/Seeded/Superseded` (3) | S44b | Emit from AdminEndpoints S33 + S34 (REOPENS L587/L1033 — same files S44 touches) |
| Employee profile | `EmployeeProfileCreated/Updated/Superseded/SoftDeleted` (4) | **S44c** (HR-sensitive payload-redaction check required at refinement) | OQ3 resolution; emit from S31+S33 EmployeeProfile endpoints. **Same-endpoint coupling**: also emits at L611 in AdminEndpoints POST /users — S44c will reopen that line |
| Local agreement profile | `LocalAgreementProfileChanged` (1) | S44c | Emit from S21 ConfigEndpoints |
| Org-targeted Payroll | `PayrollExportGenerated` (1) | **S44f** (decision: emit OR delete vestigial class) | See defined-but-unemitted section above |
| Cross-process | `RetroactiveCorrectionRequested` (1) | **S44-cross-process** | OQ2 resolution; dedicated cross-process sprint |
| ADR-025 deferred | `InstitutionProvisioned`, `InstitutionDataExported`, `UserPiiErased`, `CrossTenantReportAccessed` (4) | **Post-launch (each its own sprint per ADR-025 implementation)** | OQ4 resolution; ADR-025 was DESIGN-ONLY at S38, no endpoints exist yet |

## Validation invariant

At every future sub-sprint's refinement, re-run:
```
grep -rn "_eventStore\.AppendAsync\|IEventStore\.AppendAsync" src/Integrations src/Orchestrator src/Backend
```
to detect any NEW cross-process or in-process emit sites added since this census. New cross-process sites trigger an adjudication; new in-process sites just need the standard (conn, tx) + EnqueueAndReturnIdAsync + mapper invocation cutover.
