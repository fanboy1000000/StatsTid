using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44 / TASK-4411. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="UserUpdated"/>. TENANT_TARGETED. <see cref="UserUpdated.PrimaryOrgId"/>
/// is nullable (PUT may not change it); fallback to
/// <see cref="AuditProjectionContext.ResolvedTargetOrgId"/> which the
/// endpoint populates from the current user record before invoking the
/// mapper. Throws if both are null (endpoint contract violation).
/// </summary>
public sealed class UserUpdatedAuditMapper : IAuditProjectionMapper<UserUpdated>
{
    public AuditProjectionRowData Map(UserUpdated @event, AuditProjectionContext context)
    {
        var targetOrgId = @event.PrimaryOrgId ?? context.ResolvedTargetOrgId
            ?? throw new InvalidOperationException(
                $"UserUpdated mapper requires either @event.PrimaryOrgId or context.ResolvedTargetOrgId; both were null for UserId={@event.UserId}");

        var details = new
        {
            userId = @event.UserId,
            displayName = @event.DisplayName,
            email = @event.Email,
            primaryOrgId = @event.PrimaryOrgId,
            agreementCode = @event.AgreementCode,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: targetOrgId,
            TargetResourceId: @event.UserId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
