namespace StatsTid.Backend.Api.Contracts;

// S116 / TASK-11600 (Fork B retrofit Pass 3, PAT-010/PAT-012) — named response records for the
// self-service delegation trio (ReportingLineEndpoints endpoints 11–13). Each record is an EXACT
// shape-copy of the anonymous object its handler previously returned: same member NAMES, same
// ORDER, same nullability — serialized camelCase via the .NET 8 minimal-API
// JsonSerializerDefaults.Web default, NO [JsonPropertyName]. BYTE-IDENTICAL wire JSON.

/// <summary>One element of <see cref="DelegationStatusResponse.DelegatedEmployees"/> — the
/// dynamically re-derived covered report. <paramref name="DisplayName"/> is null when the users
/// row is missing from the batch name lookup (<c>Dictionary.GetValueOrDefault</c>).</summary>
public sealed record DelegatedEmployeeItem(
    string EmployeeId,
    string? DisplayName);

/// <summary>The <c>GET /api/reporting-lines/delegate</c> 200 body — the active self-delegation
/// status. ONE record for BOTH branches (a STABLE key set, null-vs-populated — NOT polymorphic):
/// the inactive branch emits <c>active: false</c> with null <paramref name="ActingManagerId"/>/
/// <paramref name="EffectiveFrom"/>/<paramref name="EffectiveTo"/> and an EMPTY
/// <paramref name="DelegatedEmployees"/>; the active branch populates all five. The collection is
/// non-null in both branches (no nullable-complex member — the PAT-012 nullable-$ref residual
/// stays at 2).</summary>
public sealed record DelegationStatusResponse(
    bool Active,
    string? ActingManagerId,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    IEnumerable<DelegatedEmployeeItem> DelegatedEmployees);

/// <summary>The <c>POST /api/reporting-lines/delegate</c> 200 body — the S74 contract-stable
/// creation receipt: <paramref name="DelegatedCount"/> = reports the vikar effectively covers now,
/// <paramref name="SkippedCount"/> = reports already superseded by an admin ACTING line.</summary>
public sealed record DelegationCreateResponse(
    int DelegatedCount,
    int SkippedCount,
    string ActingManagerId,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo);

/// <summary>The <c>DELETE /api/reporting-lines/delegate</c> 200 body — a GENUINE 200-with-body
/// (NOT a 204; the S115 DELETE-vikar precedent). <paramref name="RevokedCount"/> = the covered
/// reports at revoke time, or 1 when the row existed but covered none.</summary>
public sealed record DelegationRevokeResponse(
    int RevokedCount);
