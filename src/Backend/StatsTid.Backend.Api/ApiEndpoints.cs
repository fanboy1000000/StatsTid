using StatsTid.Backend.Api.Endpoints;

namespace StatsTid.Backend.Api;

/// <summary>
/// S111 / TASK-11101 — the SINGLE source of truth for the Backend.Api endpoint surface. Invoked by
/// BOTH the normal host path (after the middleware, before <c>app.Run()</c>) AND the <c>--openapi</c>
/// doc-only entrypoint (before the seeders, no DB). Sharing ONE mapping routine is load-bearing: it
/// guarantees the generated OpenAPI spec reflects the EXACT runtime endpoint set, so the committed
/// <c>openapi.json</c> can never lie about which endpoints exist (a divergence would defeat both the
/// spec≡runtime gate and the convention gate).
/// </summary>
public static class ApiEndpoints
{
    public static void MapAll(WebApplication app, bool useDbAuth)
    {
        // ── Health ──
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "backend-api" }));

        // ── Endpoint Groups ──
        app.MapAuthEndpoints(useDbAuth);
        app.MapTimeEndpoints();
        app.MapAdminEndpoints();
        app.MapUnitEndpoints(); // S104 ADR-038 D3 — units CRUD + leader designate/remove + same-Org person unit-assign
        app.MapApprovalEndpoints();
        app.MapConfigEndpoints();
        app.MapSkemaEndpoints();
        app.MapProjectEndpoints();
        app.MapAgreementConfigEndpoints();
        app.MapPositionOverrideEndpoints();
        app.MapWageTypeMappingEndpoints();
        app.MapEntitlementConfigEndpoints();
        app.MapAgreementEntitlementEndpoints();
        app.MapEmployeeProfileEndpoints();
        app.MapEntitlementEligibilityEndpoints(); // S59 / TASK-5906 — CHILD_SICK eligibility + DOB (HR-only)
        app.MapEmploymentDateEndpoints(); // S60 / TASK-6006 — employment_start_date set/read (HR-only)
        app.MapBalanceEndpoints();
        app.MapComplianceEndpoints();
        app.MapOvertimeEndpoints();
        app.MapAuditEndpoints();
        app.MapReportingLineEndpoints();
        app.MapVacationSettlementEndpoints(); // S68 ADR-033 slice 1a — §21 agreement + D10 resolve + §24 payout-pending
        app.MapTerminationPayoutRequestEndpoints(); // S71 / TASK-7102 — the §26 anmodning record + event (ADR-033 slice 3b, SPRINT-71 R6)
        app.MapSettlementReversalEndpoints();       // S71 / TASK-7102 — operator-authorized settlement reversal (ADR-033 D4/D5, SPRINT-71 R4)
    }
}
