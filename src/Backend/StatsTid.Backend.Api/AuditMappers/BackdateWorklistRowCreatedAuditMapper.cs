using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Serialization;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S138 / TASK-13803. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="BackdateWorklistRowCreated"/>. TENANT_TARGETED; target_org_id =
/// <c>context.ResolvedTargetOrgId</c> (the employee's home Organisation, resolved by the
/// repository in-tx); target_resource_id = employee_id (the employee-centric audit question
/// "what happened to this person's history?" — the worklist id rides in details). The
/// <c>action</c> discriminator separates a new row from an appended trigger.
/// Null-tolerant on every reference member (the catalog-driven visibility test constructs
/// events via <c>Activator.CreateInstance</c>, bypassing <c>required</c>).
/// </summary>
public sealed class BackdateWorklistRowCreatedAuditMapper : IAuditProjectionMapper<BackdateWorklistRowCreated>
{
    public AuditProjectionRowData Map(BackdateWorklistRowCreated @event, AuditProjectionContext context)
    {
        var details = new
        {
            action = @event.Appended ? "WORKLIST_TRIGGER_APPENDED" : "WORKLIST_ROW_CREATED",
            worklistId = @event.WorklistId,
            employeeId = @event.EmployeeId,
            rowKind = @event.Kind,
            year = @event.Year,
            month = @event.Month,
            exportId = @event.ExportId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            triggerKind = @event.TriggerKind,
            triggerEventId = @event.TriggerEventId,
            triggerEffectiveFrom = @event.TriggerEffectiveFrom,
            baselineContentHash = @event.BaselineContentHash,
            baselineSettlementSequence = @event.BaselineSettlementSequence,
            baselineSettlementState = @event.BaselineSettlementState,
            appended = @event.Appended,
            rowVersion = @event.RowVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
