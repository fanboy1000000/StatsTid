using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S120 / TASK-12000 (Fork B retrofit Pass 7, PAT-010/PAT-012) — named response records for the
// balance family (BalanceEndpoints: /summary, /series, /year-overview). Each record is an EXACT
// shape-copy of the anonymous object its handler previously returned: same member NAMES, same
// ORDER, same nullability — serialized camelCase via the .NET 8 minimal-API
// JsonSerializerDefaults.Web default, NO [JsonPropertyName]. BYTE-IDENTICAL wire JSON except the
// ONE owner-ruled delta (ruling #2: the year-overview empty-config category row gains
// settlement: null — see YearOverviewCategory).
//
// SIBLING RULE: the /summary settled-year reader (both branches) and the /year-overview
// closed-ferieår disposition emit the IDENTICAL 7-key shape (BalanceEndpoints.cs settled/pending
// branches + the closed-settlement disposition) — ONE record (SettlementDispositionInfo) serves
// all THREE construction sites (PAT-012 paved road §1).
//
// Every decimal day-count is copied verbatim from the row/computation site — never re-rounded at
// the serialization site.

/// <summary>The recorded ADR-033 settlement disposition — the shared 7-key shape of
/// <c>entitlements[].settlement</c> (/summary) AND <c>categories[].settlement</c>
/// (/year-overview). Null-valued (never key-omitted) for an unsettled year. A CLR-nullable
/// complex member on both parents — rides the S117 allOf nullable-complex wrapper.</summary>
public sealed record SettlementDispositionInfo(
    // Authority: the vacation_settlements settlement_state DB CHECK,
    // docker/postgres/init.sql:2918 (PENDING_REVIEW / SETTLED / REVERSED).
    [property: AllowedValues("PENDING_REVIEW", "SETTLED", "REVERSED")]
    string State,
    decimal TransferDays,   // §21 — to next-year carryover_in
    decimal PayoutDays,     // §24 — auto-payout
    decimal ForfeitDays,    // §34 — the D9 expiring bucket
    bool ForfeitPending,
    // Authority: the NAMED constraint vacation_settlements_review_disposition,
    // docker/postgres/init.sql:2980-2983 (FORFEIT / DEFER / MODREGNING / WAIVED /
    // FERIEHINDRING; nullable) + the legacy DROP/re-ADD convergence at init.sql:3672-3680.
    // `required`/`nullable` are ORTHOGONAL to the enum set (S113): null is admissible iff
    // nullable — the set constrains only non-null values.
    [property: AllowedValues("FORFEIT", "DEFER", "MODREGNING", "WAIVED", "FERIEHINDRING")]
    string? ReviewDisposition,
    decimal? ClaimDispositionDays); // non-null iff MODREGNING/WAIVED

/// <summary>One element of <see cref="BalanceSummaryResponse.Entitlements"/> — the 10-member
/// per-entitlement row. <paramref name="Settlement"/> is null for an unsettled year (the S117
/// allOf wrapper's list-nested application #1).</summary>
public sealed record BalanceEntitlementRow(
    string Type,
    string Label,
    decimal TotalQuota,
    decimal Earned,
    decimal Used,
    decimal Planned,
    decimal CarryoverIn,
    decimal Remaining,
    int EntitlementYear,
    SettlementDispositionInfo? Settlement);

/// <summary>The nested <c>overtimeBalance</c> member of <see cref="BalanceSummaryResponse"/> —
/// 5 members. The parent member is CLR-nullable (null when no overtime_balances row exists for
/// the year) — the S117 allOf wrapper's application #2. <paramref name="CompensationModel"/>
/// carries NO enum (the overtime compensation vocabulary is a REFUSED set — raw strings, no DB
/// CHECK authority; flagged P6 gap, S120).</summary>
public sealed record BalanceSummaryOvertimeInfo(
    decimal Accumulated,
    decimal PaidOut,
    decimal AfspadseringUsed,
    decimal Remaining,
    string CompensationModel);

/// <summary>The GET /api/balance/{employeeId}/summary 200 body — 12 scalars + the entitlement
/// rows + the nullable overtime sub-object.</summary>
public sealed record BalanceSummaryResponse(
    string EmployeeId,
    int Year,
    int Month,
    decimal FlexBalance,
    decimal FlexDelta,
    int VacationDaysUsed,
    decimal VacationDaysEntitlement,
    decimal NormHoursExpected,
    decimal NormHoursActual,
    decimal OvertimeHours,
    string AgreementCode,
    bool HasMerarbejde,
    IReadOnlyList<BalanceEntitlementRow> Entitlements,
    BalanceSummaryOvertimeInfo? OvertimeBalance);

/// <summary>One point of a <see cref="BalanceSeriesItem"/> accrual curve.
/// <paramref name="MonthEnd"/> is the handler's "yyyy-MM-dd" string (shape-copy — the handler
/// formats explicitly, unlike the DateOnly members elsewhere).</summary>
public sealed record BalanceSeriesPoint(
    string MonthEnd,
    decimal Earned,
    bool IsSelected);

/// <summary>One MONTHLY_ACCRUAL series of GET /api/balance/{employeeId}/series.
/// <paramref name="FerieaarStart"/> is the handler's "yyyy-MM-dd" string (shape-copy).</summary>
public sealed record BalanceSeriesItem(
    string Type,
    string Label,
    decimal AnnualQuota,
    int EntitlementYear,
    string FerieaarStart,
    IReadOnlyList<BalanceSeriesPoint> Points);

/// <summary>The GET /api/balance/{employeeId}/series 200 envelope.</summary>
public sealed record BalanceSeriesResponse(
    string EmployeeId,
    int Year,
    int Month,
    IReadOnlyList<BalanceSeriesItem> Series);

/// <summary>The <c>header</c> member of <see cref="YearOverviewResponse"/>.
/// <paramref name="WeeklyNormHours"/> is null when no dated profile/config covers today
/// (graceful, ADR-023 D3).</summary>
public sealed record YearOverviewHeader(
    string EmployeeName,
    string AgreementCode,
    string OkVersion,
    decimal? WeeklyNormHours);

/// <summary>The <c>tiles</c> member of <see cref="YearOverviewResponse"/> — the designed 6
/// current-balance tiles + the two eligibility affordances. The nullable remainings are null
/// when the type has no config (or the employee is ineligible).</summary>
public sealed record YearOverviewTiles(
    decimal FlexBalance,
    decimal? FerieRemaining,
    decimal? CareDayRemaining,
    decimal? SeniorDayRemaining,
    int SickDaysYtd,
    decimal? ChildSickRemaining,
    bool ChildSickEligible,
    bool SeniorDayEligible);

/// <summary>One month row of <see cref="YearOverviewResponse.Months"/>.
/// <paramref name="NormHours"/> is null when any norm-bearing day resolves null;
/// <paramref name="Diff"/> is null for future months (or null norm).</summary>
public sealed record YearOverviewMonth(
    int Month,
    decimal WorkedHours,
    decimal? NormHours,
    decimal? Diff);

/// <summary>One category row of <see cref="YearOverviewResponse.Categories"/>.
/// <paramref name="Saldo"/> is the 12-element end-of-month remaining array (null entries for
/// config-less months); <paramref name="Settlement"/> is the recorded closed-ferieår disposition
/// (null when unsettled — the S117 allOf wrapper's list-nested application #3).
/// S120 ruling #2 (branch-normalization class, 2nd instance): the empty-config graceful row now
/// ALSO carries <c>settlement: null</c> (pre-S120 it omitted the key; the configured rows always
/// emitted it) — every other member of the empty row is byte-identical.</summary>
public sealed record YearOverviewCategory(
    string Type,
    string Label,
    IReadOnlyList<decimal?> Saldo,
    IReadOnlyList<decimal> Afholdt,
    decimal Expiring,
    int BoundaryMonth,
    SettlementDispositionInfo? Settlement);

/// <summary>The GET /api/balance/{employeeId}/year-overview 200 body.
/// <paramref name="Today"/> is the handler's "yyyy-MM-dd" string (TimeProvider-derived).</summary>
public sealed record YearOverviewResponse(
    string EmployeeId,
    int Year,
    string Today,
    YearOverviewHeader Header,
    YearOverviewTiles Tiles,
    IReadOnlyList<YearOverviewMonth> Months,
    IReadOnlyList<YearOverviewCategory> Categories);
