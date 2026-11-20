using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Shared;

/// <summary>
/// S148 / TASK-14826-T (R-10, ADR-043 D12) — the one place a test builds an engine agreement
/// config from the compiled table, and the one builder of engine requests that carry it.
///
/// <para>
/// Since ADR-043 D12 the Rule Engine reads no configuration of its own: every route takes the
/// config from its request and refuses a request without one. A test that used to let the engine
/// resolve the rules now hands them over, and this helper is the hand. <see cref="Config"/> is
/// <c>CentralAgreementConfigs.GetConfig</c> with the same arguments, which is exactly what the
/// deleted <c>AgreementConfigProvider</c> forwarded to, so a repaired test gets the same object,
/// value for value, as before: no expected value moves.
/// </para>
///
/// <para>
/// One source file, compiled into both test projects (the Unit project links it; there is no shared
/// test project). It is pure: no I/O, no clock.
/// </para>
/// </summary>
public static class EngineConfigRequests
{
    /// <summary>The compiled config for an agreement code and OK version, with the compiled position override applied when a position is given.</summary>
    public static AgreementRuleConfig Config(string agreementCode, string okVersion, string? position = null) =>
        CentralAgreementConfigs.GetConfig(agreementCode, okVersion, position);

    /// <summary>The compiled config for a profile's own agreement code, OK version and position.</summary>
    public static AgreementRuleConfig ConfigFor(EmploymentProfile profile) =>
        Config(profile.AgreementCode, profile.OkVersion, profile.Position);

    /// <summary>
    /// A copy of <paramref name="config"/> with one member set. Every public settable member is
    /// copied by reflection, so a member added to <see cref="AgreementRuleConfig"/> later is carried
    /// along; a public member that cannot be set throws, so none is skipped silently.
    /// </summary>
    public static AgreementRuleConfig With(AgreementRuleConfig config, string name, object? value)
    {
        var copy = (AgreementRuleConfig)Activator.CreateInstance(typeof(AgreementRuleConfig), nonPublic: true)!;
        var found = false;
        foreach (var property in typeof(AgreementRuleConfig).GetProperties(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            var setter = property.SetMethod
                ?? throw new InvalidOperationException($"AgreementRuleConfig.{property.Name} cannot be set, so it cannot be copied.");
            if (property.Name == name)
            {
                found = true;
                setter.Invoke(copy, [value]);
            }
            else
            {
                setter.Invoke(copy, [property.GetValue(config)]);
            }
        }

        if (!found)
            throw new ArgumentException($"AgreementRuleConfig has no member {name}.", nameof(name));
        return copy;
    }

    /// <summary>One timeline piece with the compiled config of its own agreement and the OK version of its days (the piece must not span 1 April; cut it there).</summary>
    public static TimelinePieceRequest Piece(
        DateOnly from, DateOnly to, decimal fraction, string agreementCode, string? position = null) =>
        Piece(from, to, fraction, agreementCode, position,
            Config(agreementCode, OkVersionResolver.ResolveVersion(from), position));

    /// <summary>One timeline piece carrying exactly the config given (a fact about a wrong or changed config).</summary>
    public static TimelinePieceRequest Piece(
        DateOnly from, DateOnly to, decimal fraction, string agreementCode, string? position, AgreementRuleConfig? config) =>
        new()
        {
            From = from,
            To = to,
            PartTimeFraction = fraction,
            AgreementCode = agreementCode,
            Position = position,
            Config = config,
        };

    /// <summary>
    /// The compliance config runs over <c>[from, to]</c>: one run per OK version, each with that
    /// version's compiled config. With <paramref name="perDay"/> (a fact that needs a different
    /// value on some days) there is one run per day, each carrying <c>perDay(day, compiled config
    /// of that day)</c>.
    /// </summary>
    public static List<ComplianceConfigRun> ComplianceRuns(
        string agreementCode, DateOnly from, DateOnly to, string? position = null,
        Func<DateOnly, AgreementRuleConfig, AgreementRuleConfig>? perDay = null)
    {
        var runs = new List<ComplianceConfigRun>();
        if (perDay is null)
        {
            foreach (var (start, end, version) in OkVersionResolver.ResolveVersionsForPeriod(from, to))
            {
                runs.Add(Run(start, end, agreementCode, Config(agreementCode, version, position)));
            }

            return runs;
        }

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var compiled = Config(agreementCode, OkVersionResolver.ResolveVersion(day), position);
            runs.Add(Run(day, day, agreementCode, perDay(day, compiled)));
        }

        return runs;
    }

    /// <summary>One compliance run carrying exactly the config given.</summary>
    public static ComplianceConfigRun Run(DateOnly from, DateOnly to, string agreementCode, AgreementRuleConfig? config) =>
        new() { From = from, To = to, AgreementCode = agreementCode, Config = config };

    /// <summary>An <c>evaluate</c> request; the config defaults to the profile's compiled one, and null sends none.</summary>
    public static EvaluateRequest Evaluate(
        string ruleId, EmploymentProfile profile, List<TimeEntry> entries, DateOnly periodStart, DateOnly periodEnd,
        AgreementRuleConfig? config) =>
        new()
        {
            RuleId = ruleId,
            Profile = profile,
            Entries = entries,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Config = config,
        };

    /// <summary>An <c>evaluate</c> request carrying the profile's compiled config.</summary>
    public static EvaluateRequest Evaluate(
        string ruleId, EmploymentProfile profile, List<TimeEntry> entries, DateOnly periodStart, DateOnly periodEnd) =>
        Evaluate(ruleId, profile, entries, periodStart, periodEnd, ConfigFor(profile));

    /// <summary>An <c>evaluate-flex</c> request carrying the given config (null sends none).</summary>
    public static EvaluateFlexRequest EvaluateFlex(
        EmploymentProfile profile, List<TimeEntry> entries, List<AbsenceEntry> absences,
        DateOnly periodStart, DateOnly periodEnd, decimal previousBalance, AgreementRuleConfig? config) =>
        new()
        {
            Profile = profile,
            Entries = entries,
            Absences = absences,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PreviousBalance = previousBalance,
            Config = config,
        };

    /// <summary>A <c>check-overtime-governance</c> request carrying the given config (null sends none).</summary>
    public static CheckOvertimeGovernanceRequest Governance(
        EmploymentProfile profile, List<TimeEntry> entries, DateOnly periodStart, DateOnly periodEnd,
        decimal overtimeHoursInPeriod, bool hasPreApproval, AgreementRuleConfig? config) =>
        new()
        {
            Profile = profile,
            Entries = entries,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            OvertimeHoursInPeriod = overtimeHoursInPeriod,
            HasPreApproval = hasPreApproval,
            Config = config,
        };

    /// <summary>An <c>evaluate-timeline</c> request over the given pieces (each carries its own config).</summary>
    public static EvaluateTimelineRequest Timeline(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, List<TimelinePieceRequest> pieces,
        List<TimeEntry> entries, List<AbsenceEntry>? absences = null, decimal previousFlexBalance = 0m,
        DateOnly? settlementCutoff = null) =>
        new()
        {
            EmployeeId = employeeId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            SettlementCutoff = settlementCutoff,
            Pieces = pieces,
            Entries = entries,
            Absences = absences ?? [],
            PreviousFlexBalance = previousFlexBalance,
        };

    /// <summary>A <c>check-compliance</c> request over the given runs (null sends none).</summary>
    public static CheckComplianceRequest Compliance(
        EmploymentProfile profile, List<TimeEntry> entries, DateOnly periodStart, DateOnly periodEnd,
        List<ComplianceWorkInterval> workIntervals, List<ComplianceConfigRun>? runs) =>
        new()
        {
            Profile = profile,
            Entries = entries,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            WorkIntervals = workIntervals,
            Configs = runs,
        };
}
