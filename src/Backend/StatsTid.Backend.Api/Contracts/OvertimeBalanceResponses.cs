namespace StatsTid.Backend.Api.Contracts;

// S120 / TASK-12000 (Fork B retrofit Pass 7, PAT-010/PAT-012) — named response records for the
// per-employee overtime family (OvertimeEndpoints: balance GET, compensation-choice GET/PUT,
// compensate POST; the pre-approval quartet + admin list were typed at S116 in
// OvertimePreApprovalResponses.cs). Each record is an EXACT shape-copy of the anonymous object
// its handler previously returned: same member NAMES, same ORDER, same nullability — camelCase
// Web defaults, NO [JsonPropertyName]. BYTE-IDENTICAL wire JSON.
//
// GET /api/overtime/{employeeId}/governance declares `.Produces<ComplianceCheckResult>` on the
// NAMED SharedKernel model (see ComplianceResponses.cs for the enum-authority note); the S120
// ruling-#3 null→502 guard makes its 200 structurally non-null.
//
// REFUSED enum sets (S120 owner ruling, Explicit exclusions): BOTH overtime compensation
// vocabularies — compensationType (PAYOUT/AFSPADSERING) and compensationModel
// (AFSPADSERING/UDBETALING) — are raw strings with inline validation only and NO DB CHECK
// (init.sql:1293 local_configurations.default_compensation_model, init.sql:1842
// overtime_balances.compensation_model), and `source` is a pair of inline literals
// ("balance"/"config_default"). None may carry [AllowedValues] — flagged to the owner as a
// future P6 authority gap, not closed here.

/// <summary>The GET /api/overtime/{employeeId}/balance 200 body — the 10-member projection of
/// the <c>OvertimeBalance</c> model (<c>remaining</c> is the model's computed
/// accumulated − paidOut − afspadseringUsed, copied verbatim).</summary>
public sealed record OvertimeBalanceResponse(
    Guid BalanceId,
    string EmployeeId,
    string AgreementCode,
    int PeriodYear,
    decimal Accumulated,
    decimal PaidOut,
    decimal AfspadseringUsed,
    decimal Remaining,
    string CompensationModel,
    DateTime UpdatedAt);

/// <summary>The GET /api/overtime/{employeeId}/compensation-choice 200 body — ONE record for
/// BOTH branches (balance row present → <c>source: "balance"</c>; config fallback →
/// <c>source: "config_default"</c>): same 4 keys, same order — value-differing, NOT polymorphic
/// (the S120 fact-sheet pin).</summary>
public sealed record CompensationChoiceResponse(
    string EmployeeId,
    int PeriodYear,
    string CompensationModel,
    string Source);

/// <summary>The PUT /api/overtime/{employeeId}/compensation-choice 200 echo — 3 members (no
/// <c>source</c>: the write path always persists to the balance row).</summary>
public sealed record CompensationChoiceUpdateResponse(
    string EmployeeId,
    int PeriodYear,
    string CompensationModel);

/// <summary>The POST /api/overtime/{employeeId}/compensate 200 echo — the 5-member receipt
/// (both compensation branches shape-uniform). <paramref name="Applied"/> is always true on the
/// success path (shape fidelity with the prior anonymous object).</summary>
public sealed record OvertimeCompensateResponse(
    string EmployeeId,
    int PeriodYear,
    decimal Hours,
    string CompensationType,
    bool Applied);
