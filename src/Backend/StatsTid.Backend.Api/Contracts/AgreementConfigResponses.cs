using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S118 / TASK-11800 (Fork B retrofit Pass 5, PAT-010/PAT-012) — named response records for the
// agreement-config admin endpoints (AgreementConfigEndpoints). Each record is an EXACT
// shape-copy of the anonymous object its handler previously returned: same member NAMES, same
// ORDER, same nullability — serialized camelCase via the .NET 8 minimal-API
// JsonSerializerDefaults.Web default, NO [JsonPropertyName]. BYTE-IDENTICAL wire JSON on the
// 26 non-wire-changed ops; the create/clone 201s are wire-changed ONLY by owner ruling #1 (the
// dead `{configId}` fallback branch ceases to exist — the 201 is ALWAYS the full entity).
//
// [property: AllowedValues] discriminators (S113 strict-types mechanism), authorities cited:
//   status    {DRAFT, ACTIVE, ARCHIVED} — init.sql:1249 CHECK (agreement_configs.status) +
//             the CLR AgreementConfigStatus enum's total .ToString() projection + the
//             publish/archive response literals.
//   normModel {WEEKLY_HOURS, ANNUAL_ACTIVITY} — the CLR NormModel enum's total .ToString()
//             projection (no other value can be emitted).
// REFUSED (P4-open BY DESIGN, per the S118 exclusions): agreementCode / okVersion — agreement
// data, open sets.

/// <summary>
/// The 48-member agreement-config row — ONE record for the five non-by-id success sites
/// (list, by-code list, create 201, clone 201, PUT 200; the S112 sibling-record rule).
/// <c>version</c> is the row-version optimistic-concurrency token surfaced in-body so list
/// consumers can compose <c>If-Match: "&lt;version&gt;"</c> without a by-id GET.
/// The 412/409 error-body <c>currentState</c> envelopes stay anonymous/untyped (S118
/// exclusion) — they embed this record's serialization but are never declared via .Produces.
/// </summary>
public sealed record AgreementConfigResponse(
    Guid ConfigId,
    string AgreementCode,
    string OkVersion,
    [property: AllowedValues("DRAFT", "ACTIVE", "ARCHIVED")] string Status,
    long Version,
    // Norm settings
    decimal WeeklyNormHours,
    int NormPeriodWeeks,
    [property: AllowedValues("WEEKLY_HOURS", "ANNUAL_ACTIVITY")] string NormModel,
    decimal AnnualNormHours,
    // Flex settings
    decimal MaxFlexBalance,
    decimal FlexCarryoverMax,
    // Overtime settings
    bool HasOvertime,
    bool HasMerarbejde,
    decimal OvertimeThreshold50,
    decimal OvertimeThreshold100,
    // Supplement toggles
    bool EveningSupplementEnabled,
    bool NightSupplementEnabled,
    bool WeekendSupplementEnabled,
    bool HolidaySupplementEnabled,
    // Supplement time windows
    int EveningStart,
    int EveningEnd,
    int NightStart,
    int NightEnd,
    // Supplement rates
    decimal EveningRate,
    decimal NightRate,
    decimal WeekendSaturdayRate,
    decimal WeekendSundayRate,
    decimal HolidayRate,
    // On-call duty
    bool OnCallDutyEnabled,
    decimal OnCallDutyRate,
    // Call-in work
    bool CallInWorkEnabled,
    decimal CallInMinimumHours,
    decimal CallInRate,
    // Travel time
    bool TravelTimeEnabled,
    decimal WorkingTravelRate,
    decimal NonWorkingTravelRate,
    // Working time compliance
    decimal MaxDailyHours,
    decimal MinimumRestHours,
    bool RestPeriodDerogationAllowed,
    int WeeklyMaxHoursReferencePeriod,
    bool VoluntaryUnsocialHoursAllowed,
    // Metadata
    string CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? PublishedAt,
    DateTime? ArchivedAt,
    Guid? ClonedFromId,
    string? Description);

/// <summary>
/// The GET /api/agreement-configs/{configId} 200 body — the 48 row members PLUS the inline
/// <c>entitlements</c> array (each row the shared 16-member <see cref="EntitlementConfigResponse"/>,
/// carrying its own <c>version</c> for child If-Match composition) and the
/// <c>entitlementsReadOnly</c> flag (true when sibling configs share the
/// (agreement_code, ok_version) natural key). Per owner ruling #2 (S118, the drift-repair
/// class) the embedded rows now carry <c>fullDayOnly</c> — the previously-drifted inline
/// mapper omitted it (additive; display-only on the read side).
/// </summary>
public sealed record AgreementConfigWithEntitlementsResponse(
    Guid ConfigId,
    string AgreementCode,
    string OkVersion,
    [property: AllowedValues("DRAFT", "ACTIVE", "ARCHIVED")] string Status,
    long Version,
    // Norm settings
    decimal WeeklyNormHours,
    int NormPeriodWeeks,
    [property: AllowedValues("WEEKLY_HOURS", "ANNUAL_ACTIVITY")] string NormModel,
    decimal AnnualNormHours,
    // Flex settings
    decimal MaxFlexBalance,
    decimal FlexCarryoverMax,
    // Overtime settings
    bool HasOvertime,
    bool HasMerarbejde,
    decimal OvertimeThreshold50,
    decimal OvertimeThreshold100,
    // Supplement toggles
    bool EveningSupplementEnabled,
    bool NightSupplementEnabled,
    bool WeekendSupplementEnabled,
    bool HolidaySupplementEnabled,
    // Supplement time windows
    int EveningStart,
    int EveningEnd,
    int NightStart,
    int NightEnd,
    // Supplement rates
    decimal EveningRate,
    decimal NightRate,
    decimal WeekendSaturdayRate,
    decimal WeekendSundayRate,
    decimal HolidayRate,
    // On-call duty
    bool OnCallDutyEnabled,
    decimal OnCallDutyRate,
    // Call-in work
    bool CallInWorkEnabled,
    decimal CallInMinimumHours,
    decimal CallInRate,
    // Travel time
    bool TravelTimeEnabled,
    decimal WorkingTravelRate,
    decimal NonWorkingTravelRate,
    // Working time compliance
    decimal MaxDailyHours,
    decimal MinimumRestHours,
    bool RestPeriodDerogationAllowed,
    int WeeklyMaxHoursReferencePeriod,
    bool VoluntaryUnsocialHoursAllowed,
    // Metadata
    string CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? PublishedAt,
    DateTime? ArchivedAt,
    Guid? ClonedFromId,
    string? Description,
    // Inline entitlements — open rows for this (agreement_code, ok_version) pair.
    IReadOnlyList<EntitlementConfigResponse> Entitlements,
    bool EntitlementsReadOnly);

/// <summary>
/// The POST /api/agreement-configs/{configId}/publish 200 envelope. ALL keys are ALWAYS
/// emitted; <c>archivedConfigId</c>/<c>publishedAt</c> are nullable-valued
/// (NULLABLE-ALWAYS-PRESENT, never optional-key — the S113 required/nullable orthogonality).
/// <c>status</c> is the literal "ACTIVE" on this path (typed with the full status set — the
/// same init.sql:1249 authority).
/// </summary>
public sealed record AgreementConfigPublishResponse(
    Guid ConfigId,
    [property: AllowedValues("DRAFT", "ACTIVE", "ARCHIVED")] string Status,
    Guid? ArchivedConfigId,
    DateTime? PublishedAt);

/// <summary>
/// The POST /api/agreement-configs/{configId}/archive 200 envelope. ALL keys are ALWAYS
/// emitted; <c>archivedAt</c> is nullable-valued (NULLABLE-ALWAYS-PRESENT, never optional-key).
/// <c>status</c> is the literal "ARCHIVED" on this path (typed with the full status set).
/// </summary>
public sealed record AgreementConfigArchiveResponse(
    Guid ConfigId,
    [property: AllowedValues("DRAFT", "ACTIVE", "ARCHIVED")] string Status,
    DateTime? ArchivedAt);
