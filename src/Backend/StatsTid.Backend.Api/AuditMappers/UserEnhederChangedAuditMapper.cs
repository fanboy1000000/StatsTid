using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S97 / ADR-035. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="UserEnhederChanged"/>. TENANT_TARGETED. The event carries no org id (Enhed
/// is display metadata bound to the user, not an org-keyed authority fact), so the target
/// org comes from <see cref="AuditProjectionContext.ResolvedTargetOrgId"/> — the endpoint
/// populates it from the user's primary_org (the set-tags path) or the post-transfer org
/// (the transfer-clears path). Throws if null (endpoint contract violation). The details
/// JSON records the full tag-id set (an empty array = tags cleared).
/// </summary>
public sealed class UserEnhederChangedAuditMapper : IAuditProjectionMapper<UserEnhederChanged>
{
    public AuditProjectionRowData Map(UserEnhederChanged @event, AuditProjectionContext context)
    {
        var targetOrgId = context.ResolvedTargetOrgId
            ?? throw new InvalidOperationException(
                $"UserEnhederChanged mapper requires context.ResolvedTargetOrgId; it was null for UserId={@event.UserId}");

        var details = new
        {
            userId = @event.UserId,
            enhedIds = @event.EnhedIds,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: targetOrgId,
            TargetResourceId: @event.UserId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
