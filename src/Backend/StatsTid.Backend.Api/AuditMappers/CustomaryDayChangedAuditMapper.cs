using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S148 C-10 (ADR-026 D2). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="CustomaryDayChanged"/>. TENANT_TARGETED; target_org_id = <c>@event.OrgId</c> (an
/// organisation-keyed write, from the payload, not the context); target_resource_id = custom_id.
/// </summary>
public sealed class CustomaryDayChangedAuditMapper : IAuditProjectionMapper<CustomaryDayChanged>
{
    public AuditProjectionRowData Map(CustomaryDayChanged @event, AuditProjectionContext context)
    {
        var details = new
        {
            customId = @event.CustomId,
            orgId = @event.OrgId,
            action = @event.Action,
            agreementCode = @event.AgreementCode,
            recurringMonth = @event.RecurringMonth,
            recurringDay = @event.RecurringDay,
            singleDate = @event.SingleDate,
            kind = @event.Kind,
            part = @event.Part,
            partFromTime = @event.PartFromTime,
            partHours = @event.PartHours,
            paidMode = @event.PaidMode,
            validFrom = @event.ValidFrom,
            validTo = @event.ValidTo,
            precedingCustomId = @event.PrecedingCustomId,
            versionBefore = @event.VersionBefore,
            versionAfter = @event.VersionAfter,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrgId,
            TargetResourceId: @event.CustomId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
