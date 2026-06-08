using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S68 / ADR-033 D5/D10. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="SettlementManualReviewFlagged"/> — the PENDING_REVIEW signal emitted once when an
/// atomic close finds an untaken-beyond-transfer remainder that may be §34 forfeiture vs §22
/// feriehindring and which a human MUST disposition (auto-forfeiting a possibly-feriehindret
/// employee is a legal violation, ADR-033 D10). <c>FlaggedDays</c> is the un-auto-resolved
/// remainder the operator partitions; the authoritative bucket day-counts ride the resolving events.
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
public sealed class SettlementManualReviewFlaggedAuditMapper : IAuditProjectionMapper<SettlementManualReviewFlagged>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(SettlementManualReviewFlagged @event, AuditProjectionContext context)
    {
        var snapshot = @event.Snapshot;
        var details = new
        {
            kind = "SettlementManualReviewFlagged",
            disposition = "PENDING_REVIEW",
            // Settlement identity (employee, type, year, sequence) — ADR-033 D5.
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            sequence = @event.Sequence,
            // The un-auto-resolved remainder the operator must disposition (§34 vs §22) — informational.
            flaggedDays = @event.FlaggedDays,
            // Useful settle-time operands (null-tolerant — Snapshot is null on the catalog-parity instance).
            isFeriehindret = snapshot?.IsFeriehindret,
            okVersion = snapshot?.OkVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId ?? string.Empty,
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
