using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S68 / ADR-033 D5 (§34). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="VacationForfeitedToFeriefond"/> — forfeiture of untaken-not-transferred-not-paid
/// ferie days to Arbejdsmarkedets Feriefond (the S66 D9 VACATION "Til udløb" / Feriefonden-lost
/// quantity). Emitted by the operator-resolution path (or slice-4 automation), never auto pre-modeling.
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
public sealed class VacationForfeitedToFeriefondAuditMapper : IAuditProjectionMapper<VacationForfeitedToFeriefond>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(VacationForfeitedToFeriefond @event, AuditProjectionContext context)
    {
        var snapshot = @event.Snapshot;
        var details = new
        {
            kind = "VacationForfeitedToFeriefond",
            paragraph = "§34",
            // Settlement identity (employee, type, year, sequence) — ADR-033 D5.
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            sequence = @event.Sequence,
            // The §34 forfeiture bucket day-count.
            forfeitDays = @event.ForfeitDays,
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
