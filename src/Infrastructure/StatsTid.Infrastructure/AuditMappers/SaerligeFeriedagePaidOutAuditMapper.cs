using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S80 / TASK-8002 (ADR-033 Slice 2 — §15 stk.2/§17). <see cref="IAuditProjectionMapper{TEvent}"/>
/// for <see cref="SaerligeFeriedagePaidOut"/> — the særlige-feriedage (the agreement-based "6th
/// week") godtgørelse: the unused remainder of a closed SPECIAL_HOLIDAY accrual year, settled at the
/// 30-Apr-(Y+2) afholdelsesperiode boundary as a day-count payout line (SLS owns the 2½%; §17's 2½%
/// ≠ §10's 2,02% — distinct, never conflated, ADR-033 D12). Particle-free §34/§22: særlige feriedage
/// are NEVER forfeited — the whole remainder is the godtgørelse (R4).
///
/// <para>
/// TENANT_TARGETED (ADR-025 D3 — per-employee personal data, tenant-scoped); target_org_id =
/// <c>context.ResolvedTargetOrgId</c> (the BackgroundService dispatch site resolves
/// employee → <c>users.primary_org_id</c> before invoking — pure mapper, no I/O, per
/// ADR-026 D2); target_resource_id = employee_id. Mirrors the S68
/// <see cref="VacationAutoPaidOut"/> cross-process precedent.
/// </para>
///
/// <para>
/// NULL-TOLERANT of its <c>required</c> reference members (S66 <c>e0d1dc3</c> lesson): the
/// catalog-parity / per-class visibility test constructs the event via
/// <c>Activator.CreateInstance</c> (bypasses <c>required</c> init) — every reference member is
/// null-coalesced so <see cref="Map"/> never NREs.
/// </para>
/// </summary>
public sealed class SaerligeFeriedagePaidOutAuditMapper : IAuditProjectionMapper<SaerligeFeriedagePaidOut>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(SaerligeFeriedagePaidOut @event, AuditProjectionContext context)
    {
        var snapshot = @event.Snapshot;
        var details = new
        {
            kind = "SaerligeFeriedagePaidOut",
            paragraph = "§15/§17",
            // Settlement identity (employee, type, year, sequence) — ADR-033 D5.
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            sequence = @event.Sequence,
            // The §15 stk.2/§17 godtgørelse bucket day-count (the whole unused remainder).
            payoutDays = @event.PayoutDays,
            // Useful settle-time operands (null-tolerant — Snapshot is null on the catalog-parity instance).
            okVersion = snapshot?.OkVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId ?? string.Empty,
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
