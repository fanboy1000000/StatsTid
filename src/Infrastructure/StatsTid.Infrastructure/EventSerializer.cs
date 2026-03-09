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
