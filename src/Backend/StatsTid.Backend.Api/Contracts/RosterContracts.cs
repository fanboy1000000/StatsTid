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
/// <param name="ScheduledChangeFrom">S141 / TASK-14116 (refinement B0, owner requirement 2026-09-11)
/// — the date an already-scheduled employment change takes effect for this person, or <c>null</c>
/// when nothing is scheduled (which, until Increment 4's date picker ships, is every employee).
///
/// <para>WHY THIS FIELD EXISTS, in the owner's own words: <i>"Should it not be visible to an HR
/// employee looking at a page, that another has scheduled a change?"</i> It should. Wave 1 already
/// fixed the dangerous half here — <paramref name="Position"/> is now the title in force TODAY, so
/// the roster never shows a promotion's title weeks early. What was still missing is the heads-up:
/// a roster row that looks settled while a colleague has already dated a change for 1 November
/// invites HR to act on it. This field is a MARKER AND A DATE, deliberately not the change itself —
/// the roster needs to say "this changes on 1 November", not to render the new values. Read the full
/// record from the employment-history endpoint when the detail is wanted.</para>
///
/// <para>A CANCELLED scheduled change is not reported: a retired scheduled row is zero-width and
/// covers no day, so it is excluded (see <c>EmploymentTimelineSql</c>). The date spans BOTH dated
/// employment timelines — the profile (fraction / position / category) and the agreement code — so
/// the marker answers "is it safe to act on this row", not "does the job title change".</para>
///
/// <para>ADR-040 D7: this is a PROFILE or AGREEMENT-CODE effective date, never the employee's hire or
/// termination date. No row on either timeline carries an employment date.</para></param>
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
    long? PrimaryReportingLineVersion,
    DateOnly? ScheduledChangeFrom = null);

/// <summary>The away-manager's active outgoing-vikar marker (the nested object on a leader row).</summary>
public sealed record RosterOutgoingVikar(
    string VikarUserId,
    string VikarDisplayName,
    DateOnly UntilDate,
    [property: AllowedValues("FERIE", "SYGDOM", "ORLOV", "TJENESTEREJSE", "ANDET")] string Reason);

/// <summary>One DISPLAY-ONLY resolved person reference (a <c>nameResolution</c> map value).
///
/// <para>S141 / TASK-14116 (B0): <paramref name="ScheduledChangeFrom"/> carries the same marker as
/// <see cref="RosterEmployeeRow.ScheduledChangeFrom"/> — the date an already-scheduled employment
/// change takes effect, <c>null</c> when none is. A name chip shows a <paramref name="Position"/>,
/// and a job title is a label the reader acts on; it therefore owes the same heads-up a roster row
/// does. Same rule, same exclusion of a cancelled change, same ADR-040 D7 position (a profile /
/// agreement effective date, never an employment date).</para></summary>
public sealed record RosterNameRef(
    string UserId,
    string DisplayName,
    string? Position,
    string? UnitName,
    DateOnly? ScheduledChangeFrom = null);
