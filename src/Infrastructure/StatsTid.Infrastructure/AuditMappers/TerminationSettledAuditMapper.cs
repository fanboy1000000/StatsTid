using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S70 / ADR-033 D5/D9 (§26 + §7). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="TerminationSettled"/> — the composite termination settlement crystallized at the
/// employment end date (defined in S68; EMITTED from S70 slice 3a, emitted-no-consumer — the
/// Payroll §26/§7 lines are slice 3b). Carries the three §-bucket day-counts (§26 payout /
/// §7 modregning / §7 unearned-advance) — day-counts only, SLS owns all kroner (ADR-033 D1).
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
public sealed class TerminationSettledAuditMapper : IAuditProjectionMapper<TerminationSettled>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(TerminationSettled @event, AuditProjectionContext context)
    {
        var snapshot = @event.Snapshot;
        var details = new
        {
            kind = "TerminationSettled",
            paragraph = "§26+§7",
            // Settlement identity (employee, type, year, sequence) — ADR-033 D5.
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            sequence = @event.Sequence,
            // The three composite §-bucket day-counts (ADR-033 D9).
            payoutDays = @event.PayoutDays,
            modregningDays = @event.ModregningDays,
            unearnedAdvanceDays = @event.UnearnedAdvanceDays,
            // Useful settle-time operands (null-tolerant — Snapshot is null on the catalog-parity instance).
            carryoverIn = snapshot?.CarryoverIn,
            okVersion = snapshot?.OkVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId ?? string.Empty,
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
