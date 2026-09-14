using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S141 / TASK-14104 (owner ruling OQ-5 (a)). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="EmployeeProfileScheduledChangeRetired"/> — the fifth member of the EmployeeProfile
/// family and, like its four siblings, TENANT_TARGETED: <c>target_org_id</c> =
/// <c>context.ResolvedTargetOrgId</c>, <c>target_resource_id</c> = <c>employee_id</c>.
///
/// <para>
/// <b>What the details payload is FOR, since that is what decides which fields it carries.</b> The
/// owner accepted that deleting a profile also destroys a colleague's scheduled change, on condition
/// that the destruction is recorded. An auditor reading this row needs to answer three questions
/// without opening another table: what was going to change, when was it going to change, and which
/// delete destroyed it. So the payload carries the values, the interval, and
/// <c>retiredWithProfileId</c> — the causal link to the row the DELETE closed. Its visibility is the
/// same as every other profile-family row, because it is the same kind of fact about the same
/// employee.
/// </para>
/// </summary>
public sealed class EmployeeProfileScheduledChangeRetiredAuditMapper
    : IAuditProjectionMapper<EmployeeProfileScheduledChangeRetired>
{
    public AuditProjectionRowData Map(
        EmployeeProfileScheduledChangeRetired @event, AuditProjectionContext context)
    {
        var details = new
        {
            profileId = @event.ProfileId,
            employeeId = @event.EmployeeId,
            effectiveFrom = @event.EffectiveFrom,
            previousEffectiveTo = @event.PreviousEffectiveTo,
            partTimeFraction = @event.PartTimeFraction,
            position = @event.Position,
            employmentCategory = @event.EmploymentCategory,
            retiredWithProfileId = @event.RetiredWithProfileId,
            retiredOn = @event.RetiredOn,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
