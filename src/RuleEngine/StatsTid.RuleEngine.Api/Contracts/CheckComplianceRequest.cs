using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Contracts;

public sealed class CheckComplianceRequest
{
    public required EmploymentProfile Profile { get; init; }
    public required List<TimeEntry> Entries { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }

    /// <summary>
    /// The clock intervals the employee typed in Skema, over the same dates as
    /// <see cref="Entries"/> (from two days before <see cref="PeriodStart"/>: a work end on the first
    /// day is judged on the 24 hours before it, and the day before can be 23 hours long; ADR-039
    /// D5b, QUAL-253). The rest checks build their work periods from these alone; the hours checks
    /// read <see cref="Entries"/> (ADR-043 D10). An interval dated after <see cref="PeriodEnd"/> is
    /// ignored by the daily rest check; the weekly check still reads it. Required, so a caller that
    /// omits it is refused (400) rather than read as "no clock times".
    /// </summary>
    public required List<ComplianceWorkInterval> WorkIntervals { get; init; }
}

/// <summary>
/// One work interval as typed in Skema: the date it was entered on, and its start and end as
/// clock times on that date. Skema cannot hold 24:00, so a night shift arrives as two intervals
/// (22:00–23:59 on D, 00:00–06:00 on D+1), which <c>RestPeriodRule</c> joins into one work period.
/// An interval whose end is not after its start is ignored by the rule.
/// </summary>
public sealed record ComplianceWorkInterval(DateOnly Date, TimeOnly Start, TimeOnly End);
