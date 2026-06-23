using System.Text.Json;
using System.Text.Json.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

public static class EventSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Dictionary<string, Type> EventTypeMap = new()
    {
        ["TimeEntryRegistered"] = typeof(TimeEntryRegistered),
        ["NormCheckCompleted"] = typeof(NormCheckCompleted),
        // Sprint 90 (ADR-034): RESHAPED from the vestigial S22-era trace event into the
        // per-(employee, year, month) payroll-export lock fact (TASK-9001). Type name
        // preserved (the serialization key); zero historical emit sites → reshape is
        // replay-safe. Emitted per employee-month on employee-{id} from TASK-9002.
        ["PayrollExportGenerated"] = typeof(PayrollExportGenerated),
        ["IntegrationDeliveryTracked"] = typeof(IntegrationDeliveryTracked),
        ["AbsenceRegistered"] = typeof(AbsenceRegistered),
        ["FlexBalanceUpdated"] = typeof(FlexBalanceUpdated),
        ["SupplementCalculated"] = typeof(SupplementCalculated),
        ["OvertimeCalculated"] = typeof(OvertimeCalculated),
        ["PeriodCalculationCompleted"] = typeof(PeriodCalculationCompleted),
        ["RetroactiveCorrectionRequested"] = typeof(RetroactiveCorrectionRequested),
        // Sprint 6: RBAC and organizational hierarchy events
        ["OrganizationCreated"] = typeof(OrganizationCreated),
        ["OrganizationUpdated"] = typeof(OrganizationUpdated),
        ["UserCreated"] = typeof(UserCreated),
        ["UserUpdated"] = typeof(UserUpdated),
        ["RoleAssignmentGranted"] = typeof(RoleAssignmentGranted),
        ["RoleAssignmentRevoked"] = typeof(RoleAssignmentRevoked),
        ["LocalConfigurationChanged"] = typeof(LocalConfigurationChanged),
        ["PeriodSubmitted"] = typeof(PeriodSubmitted),
        ["PeriodApproved"] = typeof(PeriodApproved),
        ["PeriodRejected"] = typeof(PeriodRejected),
        // Sprint 9: Skema (monthly spreadsheet) events
        ["PeriodEmployeeApproved"] = typeof(PeriodEmployeeApproved),
        ["PeriodReopened"] = typeof(PeriodReopened),
        ["TimerCheckedIn"] = typeof(TimerCheckedIn),
        ["TimerCheckedOut"] = typeof(TimerCheckedOut),
        // Sprint 12: Agreement config management events
        ["AgreementConfigCreated"] = typeof(AgreementConfigCreated),
        ["AgreementConfigUpdated"] = typeof(AgreementConfigUpdated),
        ["AgreementConfigPublished"] = typeof(AgreementConfigPublished),
        ["AgreementConfigArchived"] = typeof(AgreementConfigArchived),
        ["AgreementConfigCloned"] = typeof(AgreementConfigCloned),
        // Sprint 14: Position override and wage type mapping events
        ["PositionOverrideCreated"] = typeof(PositionOverrideCreated),
        ["PositionOverrideUpdated"] = typeof(PositionOverrideUpdated),
        ["PositionOverrideActivated"] = typeof(PositionOverrideActivated),
        ["PositionOverrideDeactivated"] = typeof(PositionOverrideDeactivated),
        ["WageTypeMappingCreated"] = typeof(WageTypeMappingCreated),
        ["WageTypeMappingUpdated"] = typeof(WageTypeMappingUpdated),
        ["WageTypeMappingDeleted"] = typeof(WageTypeMappingDeleted),
        // Sprint 29: cross-day supersession (predecessor closed + new history row inserted) per ADR-020 D2
        ["WageTypeMappingSuperseded"] = typeof(WageTypeMappingSuperseded),
        // Sprint 15: Entitlement management events
        ["EntitlementBalanceAdjusted"] = typeof(EntitlementBalanceAdjusted),
        // Sprint 66: future-dated absence revaluation on profile change (ADR-032 D4).
        ["EntitlementBalanceRevalued"] = typeof(EntitlementBalanceRevalued),
        ["EntitlementConfigSeeded"] = typeof(EntitlementConfigSeeded),
        // Sprint 30: Entitlement config lifecycle events (ADR-020 D2 + Phase 4d-2)
        ["EntitlementConfigCreated"] = typeof(EntitlementConfigCreated),
        ["EntitlementConfigSuperseded"] = typeof(EntitlementConfigSuperseded),
        ["EntitlementConfigSoftDeleted"] = typeof(EntitlementConfigSoftDeleted),
        // Sprint 31: Employee profile lifecycle events (ADR-018 D6 + Phase 4d-3 Part 1).
        // Superseded + SoftDeleted are registered up-front but reserved for S32 emission.
        ["EmployeeProfileCreated"] = typeof(EmployeeProfileCreated),
        ["EmployeeProfileUpdated"] = typeof(EmployeeProfileUpdated),
        ["EmployeeProfileSuperseded"] = typeof(EmployeeProfileSuperseded),
        ["EmployeeProfileSoftDeleted"] = typeof(EmployeeProfileSoftDeleted),
        // Sprint 16: Working time compliance events
        ["RestPeriodViolationDetected"] = typeof(RestPeriodViolationDetected),
        ["CompensatoryRestGranted"] = typeof(CompensatoryRestGranted),
        // Sprint 17: Overtime governance events
        ["OvertimeBalanceAdjusted"] = typeof(OvertimeBalanceAdjusted),
        ["OvertimeCompensationApplied"] = typeof(OvertimeCompensationApplied),
        ["OvertimePreApprovalCreated"] = typeof(OvertimePreApprovalCreated),
        // Sprint 26: OvertimePreApproval atomic Pattern C lifecycle events (TASK-2602)
        ["OvertimePreApprovalApproved"] = typeof(OvertimePreApprovalApproved),
        ["OvertimePreApprovalRejected"] = typeof(OvertimePreApprovalRejected),
        // Sprint 20: Temporal segmentation events (ADR-016 D10)
        ["SegmentManifestCreated"] = typeof(SegmentManifestCreated),
        // Sprint 21: Local agreement profile events (ADR-017 D6/D7)
        ["LocalAgreementProfileChanged"] = typeof(LocalAgreementProfileChanged),
        // Sprint 33: User agreement-code change event (ADR-023 D2 + Phase 4e replay-data trail).
        // Emitted by AdminEndpoints PUT /api/admin/users/{userId} (TASK-3309) ONLY when
        // agreement_code mutates; rides the same atomic tx as UserUpdated.
        ["UserAgreementCodeChanged"] = typeof(UserAgreementCodeChanged),
        // Sprint 34: agreement_code versioned history (ADR-023 D2 option (b)).
        // Seeded — first-ever assignment for a user (bootstrap seeder TASK-3403 + AdminEndpoints POST TASK-3407).
        // Superseded — cross-day Case C supersession (predecessor closed + new live row created) via TASK-3407 PUT.
        ["UserAgreementCodeSeeded"] = typeof(UserAgreementCodeSeeded),
        ["UserAgreementCodeSuperseded"] = typeof(UserAgreementCodeSuperseded),
        // Sprint 40: ADR-024 role-within-agreement + correction policy + overtime authorization events.
        // RoleConfigOverride lifecycle (4) per ADR-024 D1 + ADR-020 D2 3-case routing.
        // OvertimeNecessityAcknowledged per ADR-024 D7 post-hoc necessity-ack workflow.
        // ConfigBugCorrected per ADR-024 D6 generalized correction policy.
        // MerarbejdeDiscretionary per ADR-024 D2 tri-state flag event.
        // Schema + plumbing only — no S40 production emitters; S41 cutover wires endpoint emission.
        ["RoleConfigOverrideCreated"] = typeof(RoleConfigOverrideCreated),
        ["RoleConfigOverrideUpdated"] = typeof(RoleConfigOverrideUpdated),
        ["RoleConfigOverrideSuperseded"] = typeof(RoleConfigOverrideSuperseded),
        ["RoleConfigOverrideSoftDeleted"] = typeof(RoleConfigOverrideSoftDeleted),
        ["OvertimeNecessityAcknowledged"] = typeof(OvertimeNecessityAcknowledged),
        ["ConfigBugCorrected"] = typeof(ConfigBugCorrected),
        ["MerarbejdeDiscretionary"] = typeof(MerarbejdeDiscretionary),
        // Sprint 48: Reporting line hierarchy events (Phase 5 manager delegation)
        ["ReportingLineAssigned"] = typeof(ReportingLineAssigned),
        ["ReportingLineSuperseded"] = typeof(ReportingLineSuperseded),
        ["ReportingLineBulkImported"] = typeof(ReportingLineBulkImported),
        ["ReportingLineManagerDeactivated"] = typeof(ReportingLineManagerDeactivated),
        // Sprint 51: Self-service delegation batch event (Phase 5 TASK-5104).
        // RETIRED FROM EMISSION in S74 (TASK-7401 — the manager_vikar storage cutover
        // supersedes the per-report SELF_DELEGATION fan-out), but RETAINED here for
        // historical replay: streams written under this discriminator before S74 must
        // still deserialize (replay-tested). Do NOT remove this registration.
        ["ReportingLineSelfDelegated"] = typeof(ReportingLineSelfDelegated),
        // Sprint 74: approver-owned vikar (manager_vikar) lifecycle events (ADR-027 Phase 5,
        // SPRINT-74 R4 TASK-7401). The go-forward self-delegation storage — ManagerVikarCreated
        // on POST /delegate, ManagerVikarEnded on DELETE /delegate + DelegationExpiryService
        // close. Both ride reporting-line-{absentApproverId}.
        ["ManagerVikarCreated"] = typeof(ManagerVikarCreated),
        ["ManagerVikarEnded"] = typeof(ManagerVikarEnded),
        // Sprint 49: Approval delegation fallback traversal warning (Phase 5 ADR-027 D5)
        ["FallbackTraversalWarning"] = typeof(FallbackTraversalWarning),
        // Sprint 56: Self-recorded work-time state per (employee, date) — intervals + manual hours.
        // Latest-wins superseding event; projection resolves latest (TASK-5601).
        ["WorkTimeRegistered"] = typeof(WorkTimeRegistered),
        // Sprint 59: Per-employee entitlement eligibility (CHILD_SICK) — ADR-029.
        // Dated/version-guarded superseding event; projection resolves as-of-date latest (TASK-5902/5905).
        ["EmployeeEntitlementEligibilitySet"] = typeof(EmployeeEntitlementEligibilitySet),
        // Sprint 68: Vacation-settlement event family (ADR-033 D5). Each rides employee-{id} and
        // carries the immutable settle-time snapshot + a bucket day-count + the settlement identity
        // (employee_id, entitlement_type, entitlement_year, sequence). EMITTED in slice 1a:
        // VacationCarryoverExecuted (§21), VacationAutoPaidOut (§24), VacationForfeitedToFeriefond
        // (§34), SettlementManualReviewFlagged (D10 PENDING_REVIEW). DEFINE-ONLY (contract fixed now,
        // emission automates in later slices): SettlementReversed (D4 — R10 payload extended +
        // first mapper in S71 before first emission; the slice-3b reversal service emits it),
        // FeriehindringTransferred (§22),
        // FeriehindringPaidOut (§25), SaerligeFeriedagePaidOut (§15 stk.2/§17). TerminationSettled
        // (§26+§7) is EMITTED from S70 (ADR-033 slice 3a) — emitted-no-consumer (the Payroll
        // consumer/lines are slice 3b).
        ["VacationCarryoverExecuted"] = typeof(VacationCarryoverExecuted),
        ["VacationAutoPaidOut"] = typeof(VacationAutoPaidOut),
        ["VacationForfeitedToFeriefond"] = typeof(VacationForfeitedToFeriefond),
        ["SettlementManualReviewFlagged"] = typeof(SettlementManualReviewFlagged),
        ["SettlementReversed"] = typeof(SettlementReversed),
        ["FeriehindringTransferred"] = typeof(FeriehindringTransferred),
        ["FeriehindringPaidOut"] = typeof(FeriehindringPaidOut),
        ["SaerligeFeriedagePaidOut"] = typeof(SaerligeFeriedagePaidOut),
        ["TerminationSettled"] = typeof(TerminationSettled),
        // Sprint 70: ADR-033 slice 3a — leaver lifecycle events (SPRINT-70 R10). Both ride
        // employee-{id}. EmployeeEmploymentEndDateSet = the admin set/clear/correction of
        // users.employment_end_date (+ the same-tx is_active transition, R1).
        // EmployeeEndDateDeactivationApplied = the SettlementCloseService Step-A deferred flip
        // when a future-dated end date passes (UNGATED by D13; system actor, R2).
        ["EmployeeEmploymentEndDateSet"] = typeof(EmployeeEmploymentEndDateSet),
        ["EmployeeEndDateDeactivationApplied"] = typeof(EmployeeEndDateDeactivationApplied),
        // Sprint 71: ADR-033 slice 3b — termination-emission events (SPRINT-71 R6/R10). Both ride
        // employee-{id}. TerminationPayoutRequested = the §26 anmodning fact that drives the staged
        // SLS_TBD_S26 line (Payroll consumer = TASK-7105). TerminationClaimWaived = the
        // waive-in-full resolution of a §7-shaped claim (NO line ever stages from it). The §7
        // deduct-in-full TerminationModregningApplied event is PARKED behind the SLS-dialogue task
        // (slice Step-0 gate (i) — its payload shape depends on the SLS cap answer).
        ["TerminationPayoutRequested"] = typeof(TerminationPayoutRequested),
        ["TerminationClaimWaived"] = typeof(TerminationClaimWaived),
        // Sprint 97 (ADR-035): structured Enhed metadata + multi-tag membership events.
        // PURE DISPLAY metadata — ZERO authority/scope/approval meaning. Enhed*-events ride
        // enhed-{enhedId} (latest-wins, non-temporal projection into `enheder`);
        // UserEnhederChanged rides user-{userId} (full-set idempotent overwrite into
        // `user_enheder`). Name-keyed registration; round-trips unchanged on replay.
        ["EnhedCreated"] = typeof(EnhedCreated),
        ["EnhedRenamed"] = typeof(EnhedRenamed),
        ["EnhedDeleted"] = typeof(EnhedDeleted),
        ["UserEnhederChanged"] = typeof(UserEnhederChanged),
    };

    public static string Serialize(IDomainEvent @event)
    {
        return JsonSerializer.Serialize(@event, @event.GetType(), Options);
    }

    public static IDomainEvent Deserialize(string eventType, string json)
    {
        if (!EventTypeMap.TryGetValue(eventType, out var type))
            throw new InvalidOperationException($"Unknown event type: {eventType}");

        return (IDomainEvent)JsonSerializer.Deserialize(json, type, Options)!;
    }
}
