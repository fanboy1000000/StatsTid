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
        ["UserCreated"] = typeof(UserCreated),
        ["RoleAssignmentGranted"] = typeof(RoleAssignmentGranted),
        ["RoleAssignmentRevoked"] = typeof(RoleAssignmentRevoked),
        ["LocalConfigurationChanged"] = typeof(LocalConfigurationChanged),
        ["PeriodSubmitted"] = typeof(PeriodSubmitted),
        ["PeriodApproved"] = typeof(PeriodApproved),
        ["PeriodRejected"] = typeof(PeriodRejected),
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
