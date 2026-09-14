using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S113 / TASK-11300 (PAT-012 strict-types): the [property: AllowedValues] scopeType discriminators —
// emitted as spec enums by the ResponseStrictTypesFilter. Domain = the init.sql CHECK
// (scope_type IN ('GLOBAL','ORG_ONLY')) + the grant endpoint's own validation. NOTE deliberately
// NOT declared: employmentCategory (no DB CHECK; a config-keyed OPEN set — 'Standard' by default,
// role-config overrides key new categories by data, not schema) and agreementCode/okVersion
// (agreement data, open by design).

// S112 / TASK-11201 (Fork B retrofit, PAT-010/PAT-012) — named response records for the user +
// role admin endpoints (AdminEndpoints). Each record is an EXACT shape-copy of the anonymous
// object its handler previously returned: same member NAMES, same ORDER, same nullability —
// serialized camelCase via the .NET 8 minimal-API JsonSerializerDefaults.Web default, NO
// [JsonPropertyName]. BYTE-IDENTICAL wire JSON (a dropped/renamed field is now a one-line diff
// a reviewer sees; the spec gains a real schema via .Produces<T>).

/// <summary>The POST /api/admin/users 201 body. <paramref name="Version"/> is 1 (the schema
/// DEFAULT — the ETag header carries the same value for the follow-up If-Match).</summary>
public sealed record UserCreatedResponse(
    string UserId,
    string Username,
    string DisplayName,
    string? Email,
    string PrimaryOrgId,
    string AgreementCode,
    string OkVersion,
    long Version);

/// <summary>The PUT /api/admin/users/{userId} 200 body — the post-update canonical field values
/// (sourced from the FOR-UPDATE'd row snapshot) + the bumped row version.</summary>
public sealed record UserUpdatedResponse(
    string UserId,
    string DisplayName,
    string? Email,
    string PrimaryOrgId,
    string AgreementCode,
    long Version);

/// <summary>One GET /api/admin/organizations/{orgId}/users row — a BARE ARRAY response
/// (declared <c>.Produces&lt;IEnumerable&lt;OrgUserListItem&gt;&gt;</c>; S115 / TASK-11501).
/// NEVER carries password_hash or the GDPR-gated dates. <paramref name="Version"/> is for
/// type-honesty/forward-compat only — the edit flow re-fetches the per-user GET to bind the
/// ETag before PUT.</summary>
public sealed record OrgUserListItem(
    string UserId,
    string Username,
    string DisplayName,
    string? Email,
    string PrimaryOrgId,
    string AgreementCode,
    string EmploymentCategory,
    long Version);

/// <summary>The GET /api/admin/users/{userId} 200 body (ETag-stamped read; NEVER carries
/// password_hash or the GDPR-gated dates).
///
/// <para>
/// <b>S141 / TASK-14104 (refinement B0, owner requirement 2026-09-11) — one additive, nullable
/// member: <paramref name="ScheduledAgreementCode"/>.</b> The owner's requirement is that a
/// scheduled change be visible wherever a profile is read or edited, and the agreement code is a
/// SECOND dated field on the same edit drawer, written on every save. A visibility guarantee that
/// covered only the profile fields would have left the owner's own defect one field over: HR sees
/// "AC", does not know it becomes "HK" on 1 November, re-sends what they see, and drags a
/// not-yet-effective agreement into force early. <paramref name="AgreementCode"/> remains the code
/// in force TODAY (the live <c>users.agreement_code</c> cache); the new member is what is coming.
/// </para>
/// </summary>
public sealed record UserDetailResponse(
    string UserId,
    string Username,
    string DisplayName,
    string? Email,
    string PrimaryOrgId,
    string AgreementCode,
    string OkVersion,
    string EmploymentCategory,
    long Version,
    ScheduledAgreementCodeChangeDto? ScheduledAgreementCode = null);

/// <summary>
/// S141 / TASK-14104 (refinement B0) — an agreement-code change already scheduled to take effect
/// AFTER today: the next <c>user_agreement_codes</c> row starting strictly after today, with the
/// interval it will occupy and the code it will bring.
///
/// <para>
/// <see cref="EffectiveTo"/> is <c>null</c> in the ordinary case (the change runs indefinitely); a
/// non-null value means a FURTHER change follows it, so a consumer that wants the whole future must
/// read the timeline rather than this one hop. A zero-width row is excluded upstream — it covers no
/// day and is the trace a retired scheduled change leaves behind, not a change.
/// </para>
///
/// <para>
/// <b>ADR-040 D7 check.</b> These are agreement-code effective dates, never the employee's hire or
/// termination date; no agreement-code row is dated at the hire by any writer, and the
/// employment-start floor that DOES consult the hire date refuses date-free precisely so it never
/// reaches the wire.
/// </para>
/// </summary>
public sealed record ScheduledAgreementCodeChangeDto(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string AgreementCode);

/// <summary>One GET /api/admin/users/search result row (the approver/person picker).</summary>
public sealed record UserSearchItem(
    string UserId,
    string DisplayName,
    string PrimaryOrgName);

/// <summary>The GET /api/admin/users/search envelope — <c>{ items, total, limit, offset }</c>
/// (NOT a bare array; <paramref name="Total"/> is the exact match count, which may exceed the
/// page).</summary>
public sealed record UserSearchResponse(
    IReadOnlyList<UserSearchItem> Items,
    int Total,
    int Limit,
    int Offset);

/// <summary>One role-assignment row — the element of the GET /api/admin/users/{userId}/roles
/// response, which is a BARE ARRAY (declared <c>.Produces&lt;IEnumerable&lt;UserRoleAssignmentItem&gt;&gt;</c>
/// — the envelope-vs-bare-array distinction is load-bearing, the S97/S99 bug class).
/// <paramref name="OrgId"/> is null for a GLOBAL-scoped assignment.</summary>
public sealed record UserRoleAssignmentItem(
    Guid AssignmentId,
    string RoleId,
    string? OrgId,
    [property: AllowedValues("GLOBAL", "ORG_ONLY")] string ScopeType,
    string AssignedBy,
    DateTime AssignedAt,
    DateTime? ExpiresAt);

/// <summary>The POST /api/admin/roles/grant 201 body. <paramref name="OrgId"/> is null for a
/// GLOBAL grant; <paramref name="AssignedBy"/> mirrors the actor id (nullable — the prior
/// anonymous shape emitted the raw claim).</summary>
public sealed record RoleGrantResponse(
    Guid AssignmentId,
    string UserId,
    string RoleId,
    string? OrgId,
    [property: AllowedValues("GLOBAL", "ORG_ONLY")] string ScopeType,
    string? AssignedBy,
    DateTime AssignedAt,
    DateTime? ExpiresAt);

/// <summary>The POST /api/admin/roles/revoke 200 body. <paramref name="Revoked"/> is always
/// true on the success path (carried for shape fidelity with the prior anonymous object).</summary>
public sealed record RoleRevokeResponse(
    Guid AssignmentId,
    string UserId,
    string RoleId,
    bool Revoked,
    string? RevokedBy,
    DateTime RevokedAt,
    string? Reason);
