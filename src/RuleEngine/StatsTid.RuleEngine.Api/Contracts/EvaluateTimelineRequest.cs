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
/// piece covers are not evaluated. The engine resolves each piece's config per OK-version date,
/// so a piece may cross 1 April. Server-to-server only (payroll); the pieces carry no
/// employment dates beyond what the caller already holds (ADR-040 D7 unaffected).
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
/// fraction, agreement and position. The OK version is not sent; the engine derives it from
/// the dates.
/// </summary>
public sealed class TimelinePieceRequest
{
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    public required decimal PartTimeFraction { get; init; }
    public required string AgreementCode { get; init; }
    public string? Position { get; init; }
}
