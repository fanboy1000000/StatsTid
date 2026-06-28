using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S103 / ADR-038 D10 (Enhedsspor). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="UnitRenamed"/>. TENANT_TARGETED. The event carries no Organisation id (a rename is
/// identity-bound to the unit, not an org-keyed fact), so the target org comes from
/// <see cref="AuditProjectionContext.ResolvedTargetOrgId"/> — the endpoint populates it from the
/// unit's owning Organisation. Throws if null (endpoint contract violation). Mirrors
/// <see cref="UserEnhederChangedAuditMapper"/>.
/// </summary>
public sealed class UnitRenamedAuditMapper : IAuditProjectionMapper<UnitRenamed>
{
    public AuditProjectionRowData Map(UnitRenamed @event, AuditProjectionContext context)
    {
        var targetOrgId = context.ResolvedTargetOrgId
            ?? throw new InvalidOperationException(
                $"UnitRenamed mapper requires context.ResolvedTargetOrgId; it was null for UnitId={@event.UnitId}");

        var details = new
        {
            unitId = @event.UnitId,
            newName = @event.NewName,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: targetOrgId,
            TargetResourceId: @event.UnitId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
