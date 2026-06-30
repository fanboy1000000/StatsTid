namespace StatsTid.Backend.Api.Contracts;

// S111 / TASK-11101 (Fork B typed-client, PAT-010) — named response records for the unit-tagged
// medarbejder ROSTER read GET /api/admin/reporting-lines/tree/{organisationId}/medarbejdere.
//
// These replace the anonymous object the handler previously returned. BYTE-IDENTICAL wire JSON: the
// member order below MIRRORS the prior anonymous shape EXACTLY (employeeId … primaryReportingLineVersion;
// the nested outgoingVikar / nameResolution entry orders too), serialized camelCase via the .NET 8
// minimal-API JsonSerializerDefaults.Web default — NO [JsonPropertyName]. Naming the records lets
// .Produces<RosterResponse>(200) carry a real schema (the spec source) without changing a single byte
// of the serialized response (the RosterEndpointContractTests pin that wire shape unchanged).

/// <summary>The GET …/medarbejdere envelope — <c>{ employees, pendingCountByManager, nameResolution }</c>
/// (NOT a bare array). <paramref name="PendingCountByManager"/> + <paramref name="NameResolution"/> are
/// by-id maps (serialize as JSON objects).</summary>
public sealed record RosterResponse(
    IReadOnlyList<RosterEmployeeRow> Employees,
    IReadOnlyDictionary<string, int> PendingCountByManager,
    IReadOnlyDictionary<string, RosterNameRef> NameResolution);

/// <summary>One enriched roster row. <paramref name="OutgoingVikar"/> is null-emitting (the key stays
/// present as JSON-null when the person is not an away-manager). The nullable fields
/// (<paramref name="Position"/>/<paramref name="StructuralApproverId"/>/<paramref name="OutgoingVikar"/>/
/// <paramref name="UnitId"/>/<paramref name="UnitName"/>/<paramref name="PrimaryReportingLineVersion"/>)
/// map to spec <c>nullable:true</c>, not required.</summary>
public sealed record RosterEmployeeRow(
    string EmployeeId,
    string DisplayName,
    string? Position,
    string? StructuralApproverId,
    string PeriodStatus,
    RosterOutgoingVikar? OutgoingVikar,
    bool IsRoot,
    bool IsOrphan,
    Guid? UnitId,
    string? UnitName,
    IReadOnlyList<string> LeaderIds,
    long? PrimaryReportingLineVersion);

/// <summary>The away-manager's active outgoing-vikar marker (the nested object on a leader row).</summary>
public sealed record RosterOutgoingVikar(
    string VikarUserId,
    string VikarDisplayName,
    DateOnly UntilDate,
    string Reason);

/// <summary>One DISPLAY-ONLY resolved person reference (a <c>nameResolution</c> map value).</summary>
public sealed record RosterNameRef(
    string UserId,
    string DisplayName,
    string? Position,
    string? UnitName);
