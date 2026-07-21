using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S118 / TASK-11800 (Fork B retrofit Pass 5, PAT-010/PAT-012) — named response records for the
// position-override admin endpoints (PositionOverrideEndpoints). Each record is an EXACT
// shape-copy of the anonymous object its handler previously returned: same member NAMES, same
// ORDER, same nullability — camelCase via JsonSerializerDefaults.Web, NO [JsonPropertyName].
// BYTE-IDENTICAL wire JSON except the create 201, which is wire-changed ONLY by owner ruling
// #1 (the dead `{overrideId}` fallback branch ceases to exist — the 201 is ALWAYS the full
// entity via CreateReturningAsync).
//
// [property: AllowedValues] discriminator, authority cited:
//   status {ACTIVE, INACTIVE} — init.sql:1430 CHECK (position_override_configs.status).
// REFUSED (P4-open BY DESIGN): agreementCode / okVersion / positionCode — agreement data.

/// <summary>
/// The position-override row — ONE record for the four entity-success sites (list, by-id,
/// agreement list, create 201, PUT 200; the S112 sibling-record rule). <c>version</c> is the
/// row-version optimistic-concurrency token surfaced in-body for If-Match composition.
/// The 412 error-body <c>currentState</c> envelopes stay anonymous/untyped (S118 exclusion).
/// </summary>
public sealed record PositionOverrideResponse(
    Guid OverrideId,
    string AgreementCode,
    string OkVersion,
    string PositionCode,
    [property: AllowedValues("ACTIVE", "INACTIVE")] string Status,
    // Row-version optimistic-concurrency token (TASK-2501 schema, ADR-019 pending).
    // Surfaced in body for list responses where multiple rows preclude a single ETag
    // header; by-id GET also sets the matching ETag header.
    long Version,
    decimal? MaxFlexBalance,
    decimal? FlexCarryoverMax,
    int? NormPeriodWeeks,
    decimal? WeeklyNormHours,
    string CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? Description);

/// <summary>
/// The POST /api/admin/position-overrides/{overrideId}/deactivate 200 envelope.
/// <c>deactivated</c> is always true on the success path (carried for shape fidelity with
/// the prior anonymous object); <c>status</c> is the post-transition repo status.
/// </summary>
public sealed record PositionOverrideDeactivateResponse(
    Guid OverrideId,
    [property: AllowedValues("ACTIVE", "INACTIVE")] string Status,
    bool Deactivated);

/// <summary>
/// The POST /api/admin/position-overrides/{overrideId}/activate 200 envelope.
/// <c>activated</c> is always true on the success path (shape fidelity); <c>status</c> is
/// the post-transition repo status.
/// </summary>
public sealed record PositionOverrideActivateResponse(
    Guid OverrideId,
    [property: AllowedValues("ACTIVE", "INACTIVE")] string Status,
    bool Activated);
