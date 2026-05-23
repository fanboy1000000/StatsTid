using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44 / TASK-4410. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="UserCreated"/>. TENANT_TARGETED; target_org_id =
/// <c>@event.PrimaryOrgId</c> (event payload carries the resolved value
/// from admin endpoint — no separate lookup needed); details = user
/// payload minus PrimaryOrgId (which is the target_org_id column itself).
/// </summary>
public sealed class UserCreatedAuditMapper : IAuditProjectionMapper<UserCreated>
{
    public AuditProjectionRowData Map(UserCreated @event, AuditProjectionContext context)
    {
        var details = new
        {
            userId = @event.UserId,
            username = @event.Username,
            displayName = @event.DisplayName,
            primaryOrgId = @event.PrimaryOrgId,
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.PrimaryOrgId,
            TargetResourceId: @event.UserId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
