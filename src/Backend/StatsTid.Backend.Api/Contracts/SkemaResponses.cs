using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S120 / TASK-12000 (Fork B retrofit Pass 7, PAT-010/PAT-012) — named response records for the
// skema family (SkemaEndpoints: month GET — the program's LARGEST composite — save POST,
// row-preferences PUT). Each record is an EXACT shape-copy of the anonymous object its handler
// previously returned: same member NAMES, same ORDER, same nullability — serialized camelCase
// via the .NET 8 minimal-API JsonSerializerDefaults.Web default, NO [JsonPropertyName].
// BYTE-IDENTICAL wire JSON on every path (the skema family carries NO ruled delta).
//
// SIBLING-RULE decisions (S120 fact sheet):
//   • The 4-member project row (month `projects` / `rowPreferences.projects` /
//     `catalogs.projects` / the row-prefs PUT) is BYTE-IDENTICAL to the S119
//     ProjectResponse (projectId/projectCode/projectName/sortOrder — ProjectResponses.cs) —
//     REUSED, not minted.
//   • `dailyNorm` and `consumptionBasis` rows are the identical { date, hours|null } shape —
//     ONE record (SkemaDayHoursRow).
//   • `workTime` and `boundaryWorkTime` rows are the identical day shape — ONE record.
//   • The 3-member absence-type row (legacy `absenceTypes` + `catalogs.absenceTypes` — one
//     computation, two projections) is ONE record; the 4-member preference row (adds sortOrder)
//     is a SEPARATE sibling (never extends — the S115 lie-amplifier lesson).
//   • The `approval` member was sibling-CHECKED against the S116 ApprovalResponses.cs family:
//     no existing record matches its 6-key shape (PeriodActionResponse has 2 keys,
//     EmployeePeriodItem 14, TeamOverviewEmployeeRow 18) — minted new (SkemaApprovalInfo).
//   • The row-preferences PUT 200 body is the month GET's `rowPreferences` member shape —
//     ONE record (SkemaRowPreferencesResponse) serves both surfaces.
//
// The save POST's 17-site 422/400/409 error fan stays UNTYPED (S120 Explicit exclusions).

/// <summary>One absence-type row — the 3-member shape served by BOTH the legacy top-level
/// <c>absenceTypes</c> field and <c>catalogs.absenceTypes</c> (one computation, two
/// projections — the S72-B1 anti-drift identity). <paramref name="Type"/> carries NO enum:
/// the skema absence-type strings are a REFUSED set (S119 precedent, re-ruled S120).</summary>
public sealed record SkemaAbsenceTypeRow(
    string Type,
    string Label,
    bool FullDayOnly);

/// <summary>One time-entry row of the month GET (<c>projectCode</c> maps the projection's
/// nullable TaskId).</summary>
public sealed record SkemaEntryRow(
    DateOnly Date,
    string? ProjectCode,
    decimal Hours);

/// <summary>One absence row of the month GET. <paramref name="Feriedage"/> is the ADR-032
/// recorded per-absence day-equivalent, nullable passthrough (null on zero-norm days /
/// non-entitlement rows).</summary>
public sealed record SkemaAbsenceRow(
    DateOnly Date,
    string AbsenceType,
    decimal Hours,
    decimal? Feriedage);

/// <summary>One wall-clock work interval ("HH:mm" / "HH:mm:ss" strings, no date part).</summary>
public sealed record SkemaWorkIntervalRow(
    string Start,
    string End);

/// <summary>One day's self-recorded work time — the shape of BOTH <c>workTime</c> and
/// <c>boundaryWorkTime</c> rows (identical projections; sibling rule).</summary>
public sealed record SkemaWorkTimeDayRow(
    DateOnly Date,
    IReadOnlyList<SkemaWorkIntervalRow> Intervals,
    decimal ManualHours);

/// <summary>One { date, hours|null } day row — the shape of BOTH <c>dailyNorm</c> (per-day
/// display norm; null for ANNUAL_ACTIVITY / no-profile days) and <c>consumptionBasis</c> (the
/// ADR-032 served==guard basis; null where no dated profile covers the day). Identical shapes —
/// ONE record (sibling rule).</summary>
public sealed record SkemaDayHoursRow(
    DateOnly Date,
    decimal? Hours);

/// <summary>The month GET's <c>approval</c> member — the 6-key approval-period projection.
/// CLR-nullable on the parent (null when no approval period exists for the month) — the S117
/// allOf nullable-complex wrapper's application #4. Minted NEW after the sibling-CHECK against
/// ApprovalResponses.cs (no 6-key sibling exists).</summary>
public sealed record SkemaApprovalInfo(
    Guid PeriodId,
    // Authority: the approval_periods status CHECK, docker/postgres/init.sql:1103 (5-state) —
    // the same set the S116 ApprovalResponses records declare (re-cited per the S120 Enums row).
    [property: AllowedValues("DRAFT", "SUBMITTED", "EMPLOYEE_APPROVED", "APPROVED", "REJECTED")]
    string Status,
    DateOnly? EmployeeDeadline,
    DateOnly? ManagerDeadline,
    DateTime? EmployeeApprovedAt,
    string? RejectionReason);

/// <summary>One visible absence-type row of <see cref="SkemaRowPreferencesResponse"/> — the
/// 3-member catalog row PLUS the dense effective <c>sortOrder</c>. A SEPARATE sibling of
/// <see cref="SkemaAbsenceTypeRow"/> (different wire shape — never extends).</summary>
public sealed record SkemaRowPreferenceAbsenceRow(
    string Type,
    string Label,
    bool FullDayOnly,
    int SortOrder);

/// <summary>The R4 row-preference container — served as the month GET's <c>rowPreferences</c>
/// member AND as the PUT /api/skema/{employeeId}/row-preferences 200 body (the PUT always
/// serves <c>configured: true</c>; sibling rule — one shape, two surfaces). Project rows REUSE
/// the S119 <see cref="ProjectResponse"/> (byte-identical 4-member shape).</summary>
public sealed record SkemaRowPreferencesResponse(
    bool Configured,
    IReadOnlyList<ProjectResponse> Projects,
    IReadOnlyList<SkemaRowPreferenceAbsenceRow> AbsenceTypes);

/// <summary>The month GET's <c>catalogs</c> member — the selection-INDEPENDENT addable sets
/// (org project catalog + the filtered absence-type chain).</summary>
public sealed record SkemaCatalogs(
    IReadOnlyList<ProjectResponse> Projects,
    IReadOnlyList<SkemaAbsenceTypeRow> AbsenceTypes);

/// <summary>The GET /api/skema/{employeeId}/month 200 body — the composite monthly spreadsheet
/// payload (17 top-level members). <paramref name="FullDayNormAtMonthEnd"/> is the nullable R10
/// scalar; <paramref name="Approval"/> is the nullable-complex member (S117 wrapper).</summary>
public sealed record SkemaMonthResponse(
    int Year,
    int Month,
    int DaysInMonth,
    IReadOnlyList<ProjectResponse> Projects,
    IReadOnlyList<SkemaAbsenceTypeRow> AbsenceTypes,
    IReadOnlyList<SkemaEntryRow> Entries,
    IReadOnlyList<SkemaAbsenceRow> Absences,
    IReadOnlyList<SkemaWorkTimeDayRow> WorkTime,
    IReadOnlyList<SkemaDayHoursRow> DailyNorm,
    SkemaApprovalInfo? Approval,
    DateOnly EmployeeDeadline,
    DateOnly ManagerDeadline,
    SkemaRowPreferencesResponse RowPreferences,
    SkemaCatalogs Catalogs,
    IReadOnlyList<SkemaWorkTimeDayRow> BoundaryWorkTime,
    decimal? FullDayNormAtMonthEnd,
    IReadOnlyList<SkemaDayHoursRow> ConsumptionBasis);

/// <summary>The POST /api/skema/{employeeId}/save 200 receipt — <c>{ saved }</c>, the count of
/// emitted events (entries + work-time days + absences).</summary>
public sealed record SkemaSaveResponse(
    int Saved);
