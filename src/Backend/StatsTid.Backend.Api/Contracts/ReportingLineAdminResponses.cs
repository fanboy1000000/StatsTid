namespace StatsTid.Backend.Api.Contracts;

// S115 / TASK-11501 (Fork B retrofit Pass 2, PAT-010/PAT-012) — named response records for the
// reporting-lines admin family (ReportingLineEndpoints + the period-status read in AdminEndpoints).
// Each record is an EXACT shape-copy of the anonymous object its handler previously returned: same
// member NAMES, same ORDER, same nullability — serialized camelCase via the .NET 8 minimal-API
// JsonSerializerDefaults.Web default, NO [JsonPropertyName]. BYTE-IDENTICAL wire JSON.

/// <summary>The shared reporting-line body (the <c>MapLineResponse</c> shape) — serialized by BOTH
/// branches of the two CONDITIONAL POSTs (<c>POST /api/admin/reporting-lines</c> and
/// <c>POST .../{employeeId}/acting</c>: 201 on first assignment, 200 on supersession — ONE schema,
/// two declared statuses) and by the <c>active</c>/<c>history</c> elements of
/// <see cref="EmployeeReportingLinesResponse"/>. <paramref name="EffectiveTo"/> is null on an
/// active line.</summary>
public sealed record ReportingLineResponse(
    Guid ReportingLineId,
    string EmployeeId,
    string ManagerId,
    string OrganisationId,
    string Relationship,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Source,
    long Version,
    string CreatedBy,
    DateTime CreatedAt);

/// <summary>One GET /api/admin/reporting-lines/tree/{organisationId} row — the response is a BARE
/// ARRAY (declared <c>.Produces&lt;IEnumerable&lt;ReportingLineTreeItem&gt;&gt;</c>; the
/// envelope-vs-bare-array distinction is the S97/S99 bug class). Display names are enriched via a
/// batch lookup — null when the user row is missing.</summary>
public sealed record ReportingLineTreeItem(
    Guid ReportingLineId,
    string EmployeeId,
    string? EmployeeDisplayName,
    string ManagerId,
    string? ManagerDisplayName,
    string OrganisationId,
    string Relationship,
    DateOnly EffectiveFrom,
    string Source,
    long Version);

/// <summary>The GET /api/admin/reporting-lines/{employeeId} envelope —
/// <c>{ active: [...], history: [...] }</c> (NOT a bare array), both element sets the shared
/// <see cref="ReportingLineResponse"/> shape.</summary>
public sealed record EmployeeReportingLinesResponse(
    IReadOnlyList<ReportingLineResponse> Active,
    IReadOnlyList<ReportingLineResponse> History);

/// <summary>One GET /api/admin/reporting-lines/{managerId}/reports row — a BARE ARRAY response
/// (declared <c>.Produces&lt;IEnumerable&lt;DirectReportItem&gt;&gt;</c>). Unlike the tree row it
/// carries NO managerDisplayName (only the report side is name-enriched).</summary>
public sealed record DirectReportItem(
    Guid ReportingLineId,
    string EmployeeId,
    string? EmployeeDisplayName,
    string ManagerId,
    string OrganisationId,
    string Relationship,
    DateOnly EffectiveFrom,
    string Source,
    long Version);

/// <summary>The POST /api/admin/reporting-lines/import 200 body — the per-outcome batch
/// counters (<paramref name="Total"/> = the submitted row count).</summary>
public sealed record ReportingLineImportResponse(
    int Imported,
    int Superseded,
    int Skipped,
    int Total);

/// <summary>The POST /api/admin/reporting-lines/{employeeId}/remove 200 body — the S74 R10
/// closure receipt. <paramref name="Removed"/> is the deactivated person's id; the counts come
/// from the AUTHORITATIVE in-tx census (S74-7403 B4).</summary>
public sealed record RemoveWithReassignmentResponse(
    string Removed,
    int ReportsReassigned,
    int ActingEdgesClosed);

/// <summary>The nested active-vikar body of <see cref="ActiveVikarResponse"/> — mirrors the
/// roster's <c>outgoingVikar</c> shape.</summary>
public sealed record ActiveVikarInfo(
    string VikarUserId,
    string VikarDisplayName,
    DateOnly UntilDate,
    string Reason);

/// <summary>The GET /api/admin/reporting-lines/{managerId}/vikar 200 body. The
/// <paramref name="ActiveVikar"/> member is ALWAYS emitted — <c>null</c> when the manager has no
/// active vikar, the object otherwise (a STABLE envelope with one nullable-COMPLEX member; the
/// S113 ResponseStrictTypesFilter auto-excludes it from <c>required</c> — the watched
/// nullable-$ref residual, PAT-012).</summary>
public sealed record ActiveVikarResponse(ActiveVikarInfo? ActiveVikar);

/// <summary>The POST /api/admin/reporting-lines/{managerId}/vikar 200 body — the created
/// admin-on-behalf vikar receipt. <paramref name="EffectiveTo"/> is the INCLUSIVE last-covered
/// day ("til og med", R4a).</summary>
public sealed record AdminVikarCreatedResponse(
    Guid VikarId,
    string ManagerId,
    string VikarUserId,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    string Reason);

/// <summary>The DELETE /api/admin/reporting-lines/{managerId}/vikar 200 body — a genuine
/// 200-with-body (NOT a 204). <paramref name="Revoked"/> is always true on the success path
/// (carried for shape fidelity with the prior anonymous object).</summary>
public sealed record AdminVikarRevokedResponse(
    Guid VikarId,
    string ManagerId,
    string VikarUserId,
    bool Revoked);

/// <summary>One employee row of <see cref="TreePeriodStatusResponse"/>.
/// <paramref name="Status"/> is the FE 3-state projection: OPEN / SUBMITTED / APPROVED.</summary>
public sealed record TreePeriodStatusEmployee(
    string EmployeeId,
    string DisplayName,
    string Status);

/// <summary>The GET /api/admin/reporting-lines/tree/{organisationId}/period-status envelope —
/// <c>{ employees: [...], pendingCountByManager: {...} }</c>. The dictionary is keyed by
/// manager user_id (keys pass through verbatim — no key-casing policy).</summary>
public sealed record TreePeriodStatusResponse(
    IReadOnlyList<TreePeriodStatusEmployee> Employees,
    IReadOnlyDictionary<string, int> PendingCountByManager);
