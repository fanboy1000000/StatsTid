using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
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
            // S146 TASK-14605 (C1, AC-C7): the month's recorded flex balances — what it opened and closed at and
            // where the opening came from. Null on the raw exports and every pre-S146 event, and then omitted
            // (AuditMapperJsonOptions writes no nulls), so those details are byte-identical to before.
            openingFlexBalance = @event.OpeningFlexBalance,
            closingFlexBalance = @event.ClosingFlexBalance,
            flexProvenance = @event.FlexProvenance,
            // S146 Step 7a (R-54; TASK-14610): the manifest of the calculation whose figures this export stores, so a
            // record with no lines (the employed zero-line month) still names it. Null on the raw exports and every
            // pre-fix event, and then omitted like the flex keys above. Appended LAST so every existing golden is unchanged.
            manifestId = @event.ManifestId,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
