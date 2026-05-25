using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S49 / TASK-4908. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="ReportingLineBulkImported"/>. TENANT_TARGETED; target_org_id =
/// tree_root_org_id from the event; details = batch metadata.
/// </summary>
public sealed class ReportingLineBulkImportedAuditMapper : IAuditProjectionMapper<ReportingLineBulkImported>
{
    public AuditProjectionRowData Map(ReportingLineBulkImported @event, AuditProjectionContext context)
    {
        var details = new
        {
            batchId = @event.BatchId,
            treeRootOrgId = @event.TreeRootOrgId,
            lineCount = @event.LineCount,
            source = @event.Source,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.TreeRootOrgId,
            TargetResourceId: @event.BatchId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
