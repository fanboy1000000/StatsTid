namespace StatsTid.Backend.Api.Contracts;

// S115 / TASK-11501 (Fork B retrofit Pass 2, PAT-010/PAT-012) — named response records for the
// HR employee field-endpoints (EntitlementEligibilityEndpoints + EmploymentDateEndpoints). Each
// record is an EXACT shape-copy of the anonymous object its handler previously returned: same
// member NAMES, same ORDER, same nullability — serialized camelCase via the .NET 8 minimal-API
// JsonSerializerDefaults.Web default, NO [JsonPropertyName]. BYTE-IDENTICAL wire JSON.
//
// The GET .../entitlement-eligibility/{entitlementType} read is DELIBERATELY NOT typed here —
// it is genuinely polymorphic (its no-row branch OMITS the effectiveFrom/version KEYS entirely),
// so it stays on the grandfather manifest (the S112 flag-and-defer rule's first firing; see
// tools/openapi-convention-exempt.txt).

/// <summary>The GET + PUT /api/admin/employees/{employeeId}/birth-date 200 body (HR-only DOB
/// surface; ETag carries the same version). <paramref name="BirthDate"/> is null for an unknown
/// / cleared DOB.</summary>
public sealed record BirthDateResponse(
    string EmployeeId,
    DateOnly? BirthDate,
    long Version);

/// <summary>The GET + PUT /api/admin/employees/{employeeId}/employment-start-date 200 body.
/// <paramref name="EmploymentStartDate"/> is null for an unknown / cleared start date.</summary>
public sealed record EmploymentStartDateResponse(
    string EmployeeId,
    DateOnly? EmploymentStartDate,
    long Version);

/// <summary>The GET + PUT /api/admin/employees/{employeeId}/employment-end-date 200 body (the
/// terminated-INCLUSIVE R9c surface). Carries the R1 lifecycle flags alongside the date:
/// <paramref name="EndDateDeactivated"/> marks a lifecycle-driven deactivation and
/// <paramref name="IsActive"/> the row's current active state.</summary>
public sealed record EmploymentEndDateResponse(
    string EmployeeId,
    DateOnly? EmploymentEndDate,
    bool EndDateDeactivated,
    bool IsActive,
    long Version);

/// <summary>The PUT /api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}
/// 200 body — the post-write eligibility state. <paramref name="EffectiveFrom"/> is
/// server-stamped to today UTC (ADR-023 D8); <paramref name="Version"/> also rides the ETag
/// header for the next If-Match.</summary>
public sealed record EntitlementEligibilityUpdatedResponse(
    string EmployeeId,
    string EntitlementType,
    bool Eligible,
    DateOnly EffectiveFrom,
    long Version);
