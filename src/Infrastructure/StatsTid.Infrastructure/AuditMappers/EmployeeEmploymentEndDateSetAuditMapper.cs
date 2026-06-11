using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S70 / ADR-033 slice 3a (SPRINT-70 R10). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="EmployeeEmploymentEndDateSet"/> — the admin set/clear/correction of
/// <c>users.employment_end_date</c>. Set vs clear vs correction is discriminated by the
/// old/new end-date pair (set: null→date; clear: date→null; correction: date→date); the
/// <c>is_active</c> transition (R1 same-tx lifecycle) and the row-version pair ride alongside.
///
/// <para>
/// TENANT_TARGETED (ADR-025 D3 — per-employee personal data, tenant-scoped); target_org_id =
/// <c>context.ResolvedTargetOrgId</c> (the dispatch site resolves employee →
/// <c>users.primary_org_id</c> before invoking — pure mapper, no I/O, per ADR-026 D2);
/// target_resource_id = employee_id. Mirrors the S59
/// <see cref="EmployeeEntitlementEligibilitySet"/> cross-process precedent.
/// </para>
///
/// <para>
/// NULL-TOLERANT of its <c>required</c> reference members (S66 <c>e0d1dc3</c> lesson): the
/// catalog-parity / per-class visibility test constructs the event via
/// <c>Activator.CreateInstance</c> (bypasses <c>required</c> init) — every reference member is
/// null-coalesced so <see cref="Map"/> never NREs.
/// </para>
/// </summary>
public sealed class EmployeeEmploymentEndDateSetAuditMapper : IAuditProjectionMapper<EmployeeEmploymentEndDateSet>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(EmployeeEmploymentEndDateSet @event, AuditProjectionContext context)
    {
        var details = new
        {
            kind = "EmployeeEmploymentEndDateSet",
            employeeId = @event.EmployeeId,
            // Set vs clear vs correction discriminator (null = absent under WhenWritingNull).
            oldEndDate = @event.OldEndDate,
            newEndDate = @event.NewEndDate,
            // The R1 same-tx is_active lifecycle transition.
            oldIsActive = @event.OldIsActive,
            newIsActive = @event.NewIsActive,
            // Optimistic-concurrency row-version transition (ADR-018 D7).
            versionBefore = @event.VersionBefore,
            versionAfter = @event.VersionAfter,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId ?? string.Empty,
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
