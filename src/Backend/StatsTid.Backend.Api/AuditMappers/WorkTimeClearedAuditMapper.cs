using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// <see cref="IAuditProjectionMapper{TEvent}"/> for <see cref="WorkTimeCleared"/> (addendum A1 D-5, NR-A5). The
/// cleared work-time row is kept with its marker set, but its actor columns still name whoever registered the
/// day; the audit row is what shows HR who emptied it. TENANT_TARGETED; target org =
/// <c>context.ResolvedTargetOrgId</c> (the employee's primary org); target resource = the employee id.
/// </summary>
public sealed class WorkTimeClearedAuditMapper : IAuditProjectionMapper<WorkTimeCleared>
{
    public AuditProjectionRowData Map(WorkTimeCleared @event, AuditProjectionContext context)
    {
        var details = new
        {
            employeeId = @event.EmployeeId,
            date = @event.Date,
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
