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
