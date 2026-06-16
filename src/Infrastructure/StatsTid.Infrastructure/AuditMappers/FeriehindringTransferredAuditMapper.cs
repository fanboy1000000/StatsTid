using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S79 / TASK-7901 (ADR-033 slice 4, §22 feriehindring). <see cref="IAuditProjectionMapper{TEvent}"/>
/// for <see cref="FeriehindringTransferred"/> — the §22 impediment RESCUE of the impeded tranche from
/// the §34 forfeiture bucket (<c>forfeit_days</c>) into the next entitlement year's
/// <c>carryover_in</c>. Emitted on the operator's FERIEHINDRING resolution of a PENDING_REVIEW
/// settlement (the sibling disposition to FORFEIT/WAIVED) so replay reproduces the rescued
/// day-count AND the durable impediment rationale (<see cref="FeriehindringTransferred.FeriehindringReason"/>).
/// Like §21, this is BALANCE-ONLY — no payroll line (ADR-033 D1/D7).
///
/// <para>
/// TENANT_TARGETED (ADR-025 D3 — per-employee personal data, tenant-scoped); target_org_id =
/// <c>context.ResolvedTargetOrgId</c> (the resolve endpoint resolves employee →
/// <c>users.primary_org_id</c> before invoking — pure mapper, no I/O, per ADR-026 D2);
/// target_resource_id = employee_id. Mirrors the sibling
/// <see cref="VacationForfeitedToFeriefondAuditMapper"/> / <see cref="SettlementManualReviewFlaggedAuditMapper"/>
/// settlement mappers.
/// </para>
///
/// <para>
/// NULL-TOLERANT of its <c>required</c> reference members (S66 <c>e0d1dc3</c> lesson): the
/// catalog-parity / per-class visibility test constructs the event via
/// <c>Activator.CreateInstance</c> (bypasses <c>required</c> init) — every reference member is
/// null-coalesced so <see cref="Map"/> never NREs.
/// </para>
/// </summary>
public sealed class FeriehindringTransferredAuditMapper : IAuditProjectionMapper<FeriehindringTransferred>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(FeriehindringTransferred @event, AuditProjectionContext context)
    {
        var snapshot = @event.Snapshot;
        var details = new
        {
            kind = "FeriehindringTransferred",
            paragraph = "§22",
            disposition = "FERIEHINDRING",
            // Settlement identity (employee, type, year, sequence) — ADR-033 D5.
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            sequence = @event.Sequence,
            // The §22 transfer (rescued-from-forfeiture) bucket day-count + the durable rationale.
            transferDays = @event.TransferDays,
            feriehindringReason = @event.FeriehindringReason,
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
