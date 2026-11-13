using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// <see cref="IAuditProjectionMapper{TEvent}"/> for <see cref="AbsenceCleared"/> (addendum A1 D-5, NR-A5). A
/// clear is audit-relevant where its registration sibling is not (ADR-026 D3): the retired row names whoever
/// registered it, never whoever removed it. TENANT_TARGETED; target org = <c>context.ResolvedTargetOrgId</c>
/// (the employee's primary org); target resource = the employee id. <c>clearedFeriedage</c> is left out when
/// null (the absence type draws on no balance), as the shared options omit every null.
/// </summary>
public sealed class AbsenceClearedAuditMapper : IAuditProjectionMapper<AbsenceCleared>
{
    public AuditProjectionRowData Map(AbsenceCleared @event, AuditProjectionContext context)
    {
        var details = new
        {
            employeeId = @event.EmployeeId,
            date = @event.Date,
            absenceType = @event.AbsenceType,
            clearedEventId = @event.ClearedEventId,
            clearedHours = @event.ClearedHours,
            clearedFeriedage = @event.ClearedFeriedage,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
