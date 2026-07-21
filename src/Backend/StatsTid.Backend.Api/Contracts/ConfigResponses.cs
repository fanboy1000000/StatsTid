namespace StatsTid.Backend.Api.Contracts;

// S119 / TASK-11900 (Fork B retrofit Pass 6, PAT-010/PAT-012) — named response records for the
// org-config endpoints (ConfigEndpoints). Each record is an EXACT shape-copy of the anonymous
// object its handler previously returned: same member NAMES, same ORDER, same nullability —
// serialized camelCase via the .NET 8 minimal-API JsonSerializerDefaults.Web default, NO
// [JsonPropertyName]. BYTE-IDENTICAL wire JSON on all sites (S119 is a zero-wire-change pass).
//
// ZERO [AllowedValues] declarations in this family BY DESIGN (the S119 exclusions):
//   - absence-type strings have NO DB authority (free TEXT, no CHECK — init.sql absences;
//     the set lives only in the C# AbsenceTypeLabels dict) → REFUSED;
//   - agreementCode / okVersion — agreement data, open sets (the standing refusal).
//
// The profile-PUT non-2xx surface (400 alignment/supersession, 403, 412, 428) stays
// anonymous/untyped (the S118 error-body exclusion verbatim): the 412 body embeds
// LocalAgreementProfileResponse's serialization erased to object? but is NEVER declared via
// .Produces — the exclusion boundary holds at the spec level.

/// <summary>
/// One row of GET /api/config/constraints (bare array, 13 members) — the central ACTIVE
/// agreement-config constraint reference (ADR-014). A deliberate SUBSET projection of
/// AgreementConfigEntity — do NOT merge with <see cref="EffectiveConfigResponse"/> (sibling
/// shapes differ: bare-array rows here vs object root + orgId there).
/// </summary>
public sealed record ConfigConstraintResponse(
    string AgreementCode,
    string OkVersion,
    decimal WeeklyNormHours,
    decimal MaxFlexBalance,
    decimal FlexCarryoverMax,
    bool HasOvertime,
    bool HasMerarbejde,
    bool EveningSupplementEnabled,
    bool NightSupplementEnabled,
    bool WeekendSupplementEnabled,
    bool HolidaySupplementEnabled,
    bool OnCallDutyEnabled,
    decimal OnCallDutyRate);

/// <summary>
/// The GET /api/config/{orgId} 200 body (object root, 14 members) — the effective (merged
/// central + local-override) config for the org, projected from AgreementRuleConfig plus the
/// echoed route orgId. A SEPARATE sibling of <see cref="ConfigConstraintResponse"/> — the two
/// shapes share 13 fields but differ structurally (object root + orgId vs bare-array rows);
/// merging them would be a wire change (PAT-010-forbidden).
/// </summary>
public sealed record EffectiveConfigResponse(
    string OrgId,
    string AgreementCode,
    string OkVersion,
    decimal WeeklyNormHours,
    decimal MaxFlexBalance,
    decimal FlexCarryoverMax,
    bool HasOvertime,
    bool HasMerarbejde,
    bool EveningSupplementEnabled,
    bool NightSupplementEnabled,
    bool WeekendSupplementEnabled,
    bool HolidaySupplementEnabled,
    bool OnCallDutyEnabled,
    decimal OnCallDutyRate);

/// <summary>
/// One row of GET /api/config/{orgId}/absence-types (bare array, 2 members) — a visible
/// absence type with its Danish label, sourced from the C# AbsenceTypeLabels dict filtered by
/// the org's visibility overrides. <c>type</c> carries NO enum declaration — the set has no
/// DB authority (free TEXT, no CHECK; C#-dict-only), so declaring it would over-claim.
/// </summary>
public sealed record AbsenceTypeResponse(
    string Type,
    string Label);

/// <summary>
/// The POST /api/config/{orgId}/absence-types/visibility 200 echo (3 members) — the persisted
/// visibility toggle round-tripped to the caller.
/// </summary>
public sealed record AbsenceTypeVisibilityResponse(
    string OrgId,
    string AbsenceType,
    bool IsHidden);

/// <summary>
/// The local-agreement-profile row (14 members) — ONE record for the THREE success sites
/// (PUT 200, GET 200, history-list rows; the S112 sibling-record rule), replacing the
/// MapProfileResponse anonymous shape. The five overridable members are nullable BY CONTRACT
/// (NULL = inherit central, ADR-017). <c>version</c> is the row-version optimistic-concurrency
/// token (ADR-018 D7) surfaced in-body so the frontend can round-trip it as the next If-Match;
/// the wire ETag header carries the same value RFC-7232-quoted. The 412 error-body
/// <c>currentState</c> envelope embeds this serialization erased to object? but stays
/// anonymous/UNDECLARED (the exclusion boundary).
/// </summary>
public sealed record LocalAgreementProfileResponse(
    Guid ProfileId,
    string OrgId,
    string AgreementCode,
    string OkVersion,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal? WeeklyNormHours,
    decimal? MaxFlexBalance,
    decimal? FlexCarryoverMax,
    decimal? MaxOvertimeHoursPerPeriod,
    bool? OvertimeRequiresPreApproval,
    string CreatedBy,
    DateTime CreatedAt,
    long Version);
