using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Contracts;

/// <summary>
/// Body of <c>POST /api/rules/check-compliance</c>: the working-time checks over one period. The
/// agreement configurations come in <see cref="Configs"/>, dated runs resolved by the caller,
/// because the values the checks read can change inside a period; the engine reads no
/// configuration of its own (ADR-043 D12, R-8). <see cref="Profile"/> names the employee in the
/// result; its agreement code and OK version select nothing.
/// </summary>
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

    /// <summary>
    /// The agreement configurations in force, as dated runs that do not overlap. Every date on or
    /// before <see cref="PeriodEnd"/> that carries an entry or an interval needs a run; a date with
    /// neither does not. Each check reads the value of its own day (RR-7): a work end the minimum
    /// rest of its date, a day its maximum hours and voluntary flag, a 7-day window the derogation
    /// setting of its last day. A date after <see cref="PeriodEnd"/> with no run reads its voluntary
    /// flag from the latest run. Nullable, never <c>required</c>: a request without runs is refused
    /// by the route with its fixed sentence, not by the JSON binder.
    /// </summary>
    public List<ComplianceConfigRun>? Configs { get; init; }
}

/// <summary>
/// One run of <see cref="CheckComplianceRequest.Configs"/>: the days <c>[From, To]</c> (inclusive),
/// all under one OK version, their agreement code, and the agreement configuration in force on them,
/// which must name that agreement code and OK version (A-10). The members are nullable so that a
/// run missing one is refused with the route's fixed sentence, not by the JSON binder.
/// </summary>
public sealed class ComplianceConfigRun
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? AgreementCode { get; init; }
    public AgreementRuleConfig? Config { get; init; }
}

/// <summary>
/// One work interval as typed in Skema: the date it was entered on, and its start and end as
/// clock times on that date. Skema cannot hold 24:00, so a night shift arrives as two intervals
/// (22:00–23:59 on D, 00:00–06:00 on D+1), which <c>RestPeriodRule</c> joins into one work period.
/// An interval whose end is not after its start is ignored by the rule.
/// </summary>
public sealed record ComplianceWorkInterval(DateOnly Date, TimeOnly Start, TimeOnly End);
