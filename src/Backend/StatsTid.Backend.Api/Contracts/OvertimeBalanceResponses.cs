using System.ComponentModel.DataAnnotations;

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
// DECLARED enum sets (S122 / TASK-12200, PAT-012): the S120 P6 authority gap is now closed —
// BOTH overtime compensation vocabularies carry [AllowedValues]. compensationModel
// (AFSPADSERING/UDBETALING) is authored by the two new DB CHECKs
// agreement_configs_default_compensation_model_check (init.sql agreement_configs
// .default_compensation_model — corrected: the column is on agreement_configs, NOT
// local_configurations) and overtime_balances_compensation_model_check (init.sql
// overtime_balances.compensation_model). compensationType (PAYOUT/AFSPADSERING) is authored by
// the handler VALIDATOR (OvertimeEndpoints.cs:531-532) — the first spec-enum whose authority is a
// handler-enforced check, not a DB CHECK or a total projection (S122 OQ-2). Only `source` stays
// refused — a pair of inline literals ("balance"/"config_default") with no authority.

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
    // Authority: the DB CHECK overtime_balances_compensation_model_check (init.sql
    // overtime_balances.compensation_model) — the closed model vocabulary (S122 / TASK-12200).
    [property: AllowedValues("AFSPADSERING", "UDBETALING")]
    string CompensationModel,
    DateTime UpdatedAt);

/// <summary>The GET /api/overtime/{employeeId}/compensation-choice 200 body — ONE record for
/// BOTH branches (balance row present → <c>source: "balance"</c>; config fallback →
/// <c>source: "config_default"</c>): same 4 keys, same order — value-differing, NOT polymorphic
/// (the S120 fact-sheet pin).</summary>
public sealed record CompensationChoiceResponse(
    string EmployeeId,
    int PeriodYear,
    // Authority: the DB CHECK agreement_configs_default_compensation_model_check /
    // overtime_balances_compensation_model_check (init.sql) — the closed model vocabulary
    // (S122 / TASK-12200).
    [property: AllowedValues("AFSPADSERING", "UDBETALING")]
    string CompensationModel,
    string Source);

/// <summary>The PUT /api/overtime/{employeeId}/compensation-choice 200 echo — 3 members (no
/// <c>source</c>: the write path always persists to the balance row).</summary>
public sealed record CompensationChoiceUpdateResponse(
    string EmployeeId,
    int PeriodYear,
    // Authority: the DB CHECK agreement_configs_default_compensation_model_check /
    // overtime_balances_compensation_model_check (init.sql) — the closed model vocabulary
    // (S122 / TASK-12200). Also handler-enforced at OvertimeEndpoints.cs:687.
    [property: AllowedValues("AFSPADSERING", "UDBETALING")]
    string CompensationModel);

/// <summary>The POST /api/overtime/{employeeId}/compensate 200 echo — the 5-member receipt
/// (both compensation branches shape-uniform). <paramref name="Applied"/> is always true on the
/// success path (shape fidelity with the prior anonymous object).</summary>
public sealed record OvertimeCompensateResponse(
    string EmployeeId,
    int PeriodYear,
    decimal Hours,
    // Authority (S122 OQ-2): the handler VALIDATOR OvertimeEndpoints.cs:531-532 — the per-event
    // compensationType vocabulary (PAYOUT/AFSPADSERING) has no DB column/CHECK; this is the first
    // spec-enum in a NEW "handler-enforced" authority class (PAT-012).
    [property: AllowedValues("PAYOUT", "AFSPADSERING")]
    string CompensationType,
    bool Applied);
