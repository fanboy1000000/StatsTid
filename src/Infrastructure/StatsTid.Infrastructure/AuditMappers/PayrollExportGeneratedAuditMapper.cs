using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S90 / TASK-9001 (ADR-034) — cross-process audit projection for
/// <see cref="PayrollExportGenerated"/>, the per-(employee, year, month)
/// payroll-export lock fact. Lives in Infrastructure (not Backend.Api) because
/// the event is emitted from the Payroll process (TASK-9002), mirroring
/// <see cref="RetroactiveCorrectionRequestedAuditMapper"/>. TENANT_TARGETED,
/// target resource = the employee; the endpoint pre-resolves the target org
/// (<c>employee → users.primary_org_id</c>) into the context.
/// </summary>
public sealed class PayrollExportGeneratedAuditMapper : IAuditProjectionMapper<PayrollExportGenerated>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(PayrollExportGenerated @event, AuditProjectionContext context)
    {
        var details = new
        {
            action = "PAYROLL_EXPORTED",
            employeeId = @event.EmployeeId,
            year = @event.Year,
            month = @event.Month,
            exportId = @event.ExportId,
            contentHash = @event.ContentHash,
            exportedAt = @event.ExportedAt,
            periodId = @event.PeriodId,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
