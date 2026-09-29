using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Serialization;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S138 / TASK-13803. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="BackdateWorklistRowResolved"/>. TENANT_TARGETED; target_org_id =
/// <c>context.ResolvedTargetOrgId</c>; target_resource_id = employee_id. This projection row +
/// the event ARE the audit record of an HR resolution (no <c>*_audit</c> table — refinement W8):
/// who resolved which row, with which verb (RECALCULATED / DISMISSED / HANDLED_MANUALLY) and
/// reason, which blocks were in force (<c>blockedBy</c>, S144), across which version transition
/// (ADR-019 D8). Null-tolerant on every reference member.
/// </summary>
public sealed class BackdateWorklistRowResolvedAuditMapper : IAuditProjectionMapper<BackdateWorklistRowResolved>
{
    public AuditProjectionRowData Map(BackdateWorklistRowResolved @event, AuditProjectionContext context)
    {
        var details = new
        {
            action = "WORKLIST_ROW_RESOLVED",
            worklistId = @event.WorklistId,
            employeeId = @event.EmployeeId,
            rowKind = @event.Kind,
            year = @event.Year,
            month = @event.Month,
            exportId = @event.ExportId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            resolution = @event.Resolution,
            reason = @event.Reason,
            triggerCount = @event.TriggerCount,
            // S144: the block set in force at resolution ([] when nothing blocked). Null only on a
            // pre-S144 event, where the WhenWritingNull options omit it (nothing to project).
            blockedBy = @event.BlockedBy,
            versionBefore = @event.VersionBefore,
            versionAfter = @event.VersionAfter,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
