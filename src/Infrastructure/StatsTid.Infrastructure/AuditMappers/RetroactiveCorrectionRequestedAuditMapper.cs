using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

public sealed class RetroactiveCorrectionRequestedAuditMapper : IAuditProjectionMapper<RetroactiveCorrectionRequested>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(RetroactiveCorrectionRequested @event, AuditProjectionContext context)
    {
        var details = new
        {
            employeeId = @event.EmployeeId,
            originalPeriodStart = @event.OriginalPeriodStart,
            originalPeriodEnd = @event.OriginalPeriodEnd,
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
            reason = @event.Reason,
            correctedByActorId = @event.CorrectedByActorId,
            correctionLineCount = @event.CorrectionLineCount,
            totalDifferenceHours = @event.TotalDifferenceHours,
            manifestId = @event.ManifestId,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
