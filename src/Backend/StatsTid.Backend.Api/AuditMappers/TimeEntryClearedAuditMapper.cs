using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// <see cref="IAuditProjectionMapper{TEvent}"/> for <see cref="TimeEntryCleared"/> (addendum A1 D-5, NR-A5). A
/// clear is audit-relevant where its registration sibling is not (ADR-026 D3): the retired row names whoever
/// registered it, never whoever removed it, so the audit row is what shows HR who emptied the cell.
/// TENANT_TARGETED; target org = <c>context.ResolvedTargetOrgId</c> (the employee's primary org, resolved by the
/// Skema save); target resource = the employee id.
/// </summary>
public sealed class TimeEntryClearedAuditMapper : IAuditProjectionMapper<TimeEntryCleared>
{
    public AuditProjectionRowData Map(TimeEntryCleared @event, AuditProjectionContext context)
    {
        var details = new
        {
            employeeId = @event.EmployeeId,
            date = @event.Date,
            taskId = @event.TaskId,
            clearedEventId = @event.ClearedEventId,
            clearedHours = @event.ClearedHours,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
