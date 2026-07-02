namespace StatsTid.Backend.Api.Contracts;

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

/// <summary>The GET /api/admin/users/{userId} 200 body (ETag-stamped read; NEVER carries
/// password_hash or the GDPR-gated dates).</summary>
public sealed record UserDetailResponse(
    string UserId,
    string Username,
    string DisplayName,
    string? Email,
    string PrimaryOrgId,
    string AgreementCode,
    string OkVersion,
    string EmploymentCategory,
    long Version);

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
    string ScopeType,
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
    string ScopeType,
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
