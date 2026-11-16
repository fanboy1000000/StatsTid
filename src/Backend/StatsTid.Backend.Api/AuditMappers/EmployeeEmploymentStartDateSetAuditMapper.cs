using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S148 P-28 (ADR-026 D2). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="EmployeeEmploymentStartDateSet"/>. TENANT_TARGETED; target_org_id = the context's
/// resolved target organisation; target_resource_id = the employee id. The dates are in the admin
/// audit row only (ADR-040 D7: no employee-facing body).
/// </summary>
public sealed class EmployeeEmploymentStartDateSetAuditMapper : IAuditProjectionMapper<EmployeeEmploymentStartDateSet>
{
    public AuditProjectionRowData Map(EmployeeEmploymentStartDateSet @event, AuditProjectionContext context)
    {
        var details = new
        {
            employeeId = @event.EmployeeId,
            oldStartDate = @event.OldStartDate,
            newStartDate = @event.NewStartDate,
            versionBefore = @event.VersionBefore,
            versionAfter = @event.VersionAfter,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
