using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S49 / TASK-4908. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="ReportingLineBulkImported"/>. TENANT_TARGETED; target_org_id =
/// organisation_id from the event; details = batch metadata.
/// </summary>
public sealed class ReportingLineBulkImportedAuditMapper : IAuditProjectionMapper<ReportingLineBulkImported>
{
    public AuditProjectionRowData Map(ReportingLineBulkImported @event, AuditProjectionContext context)
    {
        var details = new
        {
            batchId = @event.BatchId,
            organisationId = @event.OrganisationId,
            lineCount = @event.LineCount,
            source = @event.Source,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrganisationId,
            TargetResourceId: @event.BatchId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
