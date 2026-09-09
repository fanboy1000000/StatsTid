using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S113 / TASK-11300 (PAT-012 strict-types): [property: AllowedValues] closed-set discriminators —
// emitted as spec enums by the ResponseStrictTypesFilter (→ TS literal unions).
// - periodStatus: the FE 3-state projection ApprovalPeriodRepository.ProjectStatus — a TOTAL
//   function ("APPROVED"→APPROVED; "SUBMITTED"/"EMPLOYEE_APPROVED"→SUBMITTED; everything else incl.
//   null/DRAFT/REJECTED→OPEN), so the set is exhaustive by construction (the roster fallback is
//   also "OPEN").
// - vikar reason: the init.sql CHECK (reason IN ('FERIE','SYGDOM','ORLOV','TJENESTEREJSE','ANDET')).

// S111 / TASK-11101 (Fork B typed-client, PAT-010) — named response records for the unit-tagged
// medarbejder ROSTER read GET /api/admin/reporting-lines/tree/{organisationId}/medarbejdere.
//
// These replace the anonymous object the handler previously returned. BYTE-IDENTICAL wire JSON: the
// member order below MIRRORS the prior anonymous shape EXACTLY (employeeId … primaryReportingLineVersion;
// the nested outgoingVikar / nameResolution entry orders too), serialized camelCase via the .NET 8
// minimal-API JsonSerializerDefaults.Web default — NO [JsonPropertyName]. Naming the records lets
// .Produces<RosterResponse>(200) carry a real schema (the spec source) without changing a single byte
// of the serialized response (the RosterEndpointContractTests pin that wire shape unchanged).

/// <summary>The GET …/medarbejdere envelope — <c>{ employees, pendingCountByManager,
/// pendingPastDeadlineCountByManager, nameResolution }</c> (NOT a bare array). The three maps are
/// by-id (they serialize as JSON objects).</summary>
/// <param name="PendingCountByManager">manager user_id → how many of that manager's reports hold a
/// period awaiting them. UNCHANGED meaning.</param>
/// <param name="PendingPastDeadlineCountByManager">S140 / TASK-14004 (QUAL-163) — the SUBSET of
/// <paramref name="PendingCountByManager"/> that is PAST the manager deadline (month-end + 5, the
/// ratified provisional institutional default; computed as a fallback for period rows created
/// before the deadline columns existed, never assumed on time). A manager with no late month is
/// absent from the map — read a missing key as zero.
///
/// <para>WHY THIS FIELD EXISTS: the organisation page's "efter frist" ("past deadline") tile has
/// always been driven by <paramref name="PendingCountByManager"/>, i.e. it captioned "past
/// deadline" over a number that meant "pending" — because nothing in the system read the deadlines
/// the send flow stores. This field is the number the caption claims; the tile can now read
/// "Ikke godkendt N — heraf M efter frist" with both halves true.</para></param>
public sealed record RosterResponse(
    IReadOnlyList<RosterEmployeeRow> Employees,
    IReadOnlyDictionary<string, int> PendingCountByManager,
    IReadOnlyDictionary<string, int> PendingPastDeadlineCountByManager,
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
    [property: AllowedValues("OPEN", "SUBMITTED", "APPROVED")] string PeriodStatus,
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
    [property: AllowedValues("FERIE", "SYGDOM", "ORLOV", "TJENESTEREJSE", "ANDET")] string Reason);

/// <summary>One DISPLAY-ONLY resolved person reference (a <c>nameResolution</c> map value).</summary>
public sealed record RosterNameRef(
    string UserId,
    string DisplayName,
    string? Position,
    string? UnitName);
