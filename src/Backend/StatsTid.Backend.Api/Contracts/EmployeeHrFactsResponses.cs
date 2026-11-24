using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// [property: AllowedValues] discriminator (PAT-012 strict types): overtimeRegime is the init.sql CHECK
// on employee_hr_facts.overtime_regime {OVERTIME, EXEMPT, CONTRACTUAL, DISCRETIONARY, NONE}.

/// <summary>
/// GET /api/admin/employees/{employeeId}/hr-facts 200 body: a person's HR facts as of <c>asOf</c>.
/// <c>agreementCode</c> is the agreement in force that day; <c>allowedOvertimeRegimes</c> the values it
/// allows (empty when none); <c>overtimeRegime</c> the recorded value when that agreement allows it,
/// else null ("not recorded"); <c>onFlexScheme</c> as recorded (null = not recorded);
/// <c>effectiveFrom</c> the start of the row covering the day, null when none does.
/// <c>liveVersion</c> and <c>liveEffectiveFrom</c> are the open row's (the next write's <c>If-Match</c>
/// and its earliest date), null when no row is open. Nullable members are present as null.
/// </summary>
public sealed record EmployeeHrFactsResponse(
    string EmployeeId,
    DateOnly AsOf,
    string? AgreementCode,
    IReadOnlyList<string> AllowedOvertimeRegimes,
    [property: AllowedValues("OVERTIME", "EXEMPT", "CONTRACTUAL", "DISCRETIONARY", "NONE")] string? OvertimeRegime,
    bool? OnFlexScheme,
    DateOnly? EffectiveFrom,
    long? LiveVersion,
    DateOnly? LiveEffectiveFrom);

/// <summary>
/// PUT /api/admin/employees/{employeeId}/hr-facts 200 body: the open row after the save (the row as it
/// was when the save changed nothing). <c>version</c> also rides the <c>ETag</c> header for the next
/// <c>If-Match</c>.
/// </summary>
public sealed record EmployeeHrFactsUpdatedResponse(
    string EmployeeId,
    Guid FactsId,
    DateOnly EffectiveFrom,
    [property: AllowedValues("OVERTIME", "EXEMPT", "CONTRACTUAL", "DISCRETIONARY", "NONE")] string? OvertimeRegime,
    bool? OnFlexScheme,
    long Version);
