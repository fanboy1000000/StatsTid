using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S74 / ADR-027 Phase 5 (SPRINT-74 R4, TASK-7401). <see cref="IAuditProjectionMapper{TEvent}"/>
/// for <see cref="ManagerVikarEnded"/> — the approver-owned vikar closed either by the
/// explicit <c>DELETE /api/reporting-lines/delegate</c> revoke (<c>EndReason=REVOKED</c>)
/// or by the <c>DelegationExpiryService</c> the day AFTER its inclusive UntilDate
/// (<c>EndReason=EXPIRED</c>, R4a). TENANT_TARGETED; target_org_id = <c>tree_root_org_id</c>
/// carried on the event; target_resource_id = the vikar_id. Mirrors the S48
/// <see cref="ReportingLineSuperseded"/> mapper precedent.
///
/// <para>
/// NULL-TOLERANT of its <c>required</c> reference members (S66 lesson) — the per-class
/// visibility test constructs via uninitialized-object.
/// </para>
/// </summary>
public sealed class ManagerVikarEndedAuditMapper : IAuditProjectionMapper<ManagerVikarEnded>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(ManagerVikarEnded @event, AuditProjectionContext context)
    {
        var details = new
        {
            kind = "ManagerVikarEnded",
            vikarId = @event.VikarId,
            absentApproverId = @event.AbsentApproverId,
            vikarUserId = @event.VikarUserId,
            untilDate = @event.UntilDate.ToString("yyyy-MM-dd"),
            effectiveTo = @event.EffectiveTo.ToString("yyyy-MM-dd"),
            endReason = @event.EndReason,
            reason = @event.Reason,
            treeRootOrgId = @event.TreeRootOrgId,
            rowVersion = @event.RowVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.TreeRootOrgId ?? context.ResolvedTargetOrgId,
            TargetResourceId: @event.VikarId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
