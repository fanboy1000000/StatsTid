using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S74 / ADR-027 Phase 5 (SPRINT-74 R4, TASK-7401). <see cref="IAuditProjectionMapper{TEvent}"/>
/// for <see cref="ManagerVikarCreated"/> — the approver-owned vikar created on
/// <c>POST /api/reporting-lines/delegate</c>. TENANT_TARGETED; target_org_id =
/// <c>organisation_id</c> carried on the event (mirrors the S48
/// <see cref="ReportingLineAssigned"/> mapper — the event already carries the tree root,
/// so no <c>context.ResolvedTargetOrgId</c> lookup is needed); target_resource_id =
/// the vikar_id.
///
/// <para>
/// NULL-TOLERANT of its <c>required</c> reference members (S66 lesson): the catalog-parity
/// per-class visibility test constructs the event via uninitialized-object so every
/// reference member is null-coalesced so <see cref="Map"/> never NREs.
/// </para>
/// </summary>
public sealed class ManagerVikarCreatedAuditMapper : IAuditProjectionMapper<ManagerVikarCreated>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(ManagerVikarCreated @event, AuditProjectionContext context)
    {
        var details = new
        {
            kind = "ManagerVikarCreated",
            vikarId = @event.VikarId,
            absentApproverId = @event.AbsentApproverId,
            vikarUserId = @event.VikarUserId,
            untilDate = @event.UntilDate.ToString("yyyy-MM-dd"),
            reason = @event.Reason,
            organisationId = @event.OrganisationId,
            rowVersion = @event.RowVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrganisationId ?? context.ResolvedTargetOrgId,
            TargetResourceId: @event.VikarId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
