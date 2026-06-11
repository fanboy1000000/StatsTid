using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S70 / ADR-033 slice 3a (SPRINT-70 R2). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="EmployeeEndDateDeactivationApplied"/> — the <c>SettlementCloseService</c> Step-A
/// deferred deactivation flip when a future-dated <c>employment_end_date</c> passes
/// (<c>is_active=false</c> + <c>end_date_deactivated=true</c> provenance + version bump,
/// UNGATED by the D13 go-live gate; system actor).
///
/// <para>
/// TENANT_TARGETED (ADR-025 D3 — per-employee personal data, tenant-scoped); target_org_id =
/// <c>context.ResolvedTargetOrgId</c> (the BackgroundService dispatch site resolves
/// employee → <c>users.primary_org_id</c> before invoking — pure mapper, no I/O, per
/// ADR-026 D2); target_resource_id = employee_id. Mirrors the S59
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
public sealed class EmployeeEndDateDeactivationAppliedAuditMapper : IAuditProjectionMapper<EmployeeEndDateDeactivationApplied>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(EmployeeEndDateDeactivationApplied @event, AuditProjectionContext context)
    {
        var details = new
        {
            kind = "EmployeeEndDateDeactivationApplied",
            employeeId = @event.EmployeeId,
            // The passed employment_end_date that triggered the flip (the LAST day employed, R1).
            endDate = @event.EndDate,
            // The flip's is_active transition (predicate-guarded TRUE→FALSE, R2).
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
