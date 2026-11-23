using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Contracts;

/// <summary>
/// S145 / TASK-14505 — body of <c>POST /api/rules/evaluate-timeline</c>: one call evaluates
/// overtime / merarbejde, the norm check and flex for a pay period, week by week.
///
/// <para>
/// <c>PeriodStart..PeriodEnd</c> is the pay period. <c>SettlementCutoff</c> is the last day
/// evaluated when it falls inside the period's last week (a leaver's last employed day), else
/// null. <c>Pieces</c> cover every employed day to evaluate — the period's days and the context
/// days of its opening week (the previous month's days of a week this period settles); days no
/// piece covers are not evaluated. Each piece carries its own agreement configuration, resolved by
/// the caller; the engine reads none of its own (ADR-043 D12, R-8). A config names one OK version,
/// so a piece lies inside one OK version: the caller cuts at 1 April, and a piece across it is
/// refused. Server-to-server only (payroll); the pieces carry no employment dates beyond what the
/// caller already holds (ADR-040 D7 unaffected).
/// </para>
/// </summary>
public sealed class EvaluateTimelineRequest
{
    public required string EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public DateOnly? SettlementCutoff { get; init; }
    public required List<TimelinePieceRequest> Pieces { get; init; }
    public required List<TimeEntry> Entries { get; init; }
    public required List<AbsenceEntry> Absences { get; init; }

    /// <summary>The opening flex balance. Required: a missing balance must never read as 0.</summary>
    public required decimal PreviousFlexBalance { get; init; }
}

/// <summary>
/// One stretch of the timeline: the days <c>[From, To]</c> (inclusive) under one part-time
/// fraction, agreement, position and agreement configuration. The OK version is not sent; the
/// engine derives it from the dates, and the piece's days must all fall under one (A-10).
/// </summary>
public sealed class TimelinePieceRequest
{
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    public required decimal PartTimeFraction { get; init; }
    public required string AgreementCode { get; init; }
    public string? Position { get; init; }

    /// <summary>
    /// The agreement configuration in force on the piece's days, resolved by the caller for its
    /// agreement, OK version and position (ADR-043 D12, R-8). It must name the piece's agreement
    /// code and the OK version of its days (A-10). Nullable, never <c>required</c>: a piece without
    /// one is refused with the route's fixed sentence, not by the JSON binder.
    /// </summary>
    public AgreementRuleConfig? Config { get; init; }
}
