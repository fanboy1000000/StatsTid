using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Shared;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S148 TASK-14826-T, AC-C1j - a rest is judged by the minimum rest in force on the day the work ends.
///
/// <para>
/// <b>Plain language.</b> The compliance check now receives the agreement rules as dated runs, because
/// the minimum rest can change inside a month. A work end on Monday is judged by Monday's minimum,
/// not Sunday's: after a change from 11 h to 12 h on Monday, an 11.5 h rest that ends on Monday is a
/// breach, one that ends on Sunday is not; after a change from 12 h to 11 h the reverse holds.
/// </para>
/// </summary>
public class RestLaterDayRuleTests
{
    private const string S10 = "Every day up to PeriodEnd that carries an entry or an interval needs a config run.";

    private static readonly DateOnly March1 = new(2026, 3, 1);
    private static readonly DateOnly March31 = new(2026, 3, 31);
    private static readonly DateOnly Sat14 = new(2026, 3, 14);
    private static readonly DateOnly Sun15 = new(2026, 3, 15);
    private static readonly DateOnly Mon16 = new(2026, 3, 16);

    private static EmploymentProfile Profile() => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = "AC",
        OkVersion = "OK24",
        EmploymentCategory = "STANDARD",
    };

    private static ComplianceWorkInterval Iv(DateOnly date, int startH, int startM, int endH, int endM) =>
        new(date, new TimeOnly(startH, startM), new TimeOnly(endH, endM));

    // Saturday 10:00-20:30; Sunday 08:00-14:00 and 16:00-20:30; Monday 08:00-14:00. Every judged end
    // (Sunday 14:00, Sunday 20:30, Monday 14:00) has an unbroken 11.5 h rest in the 24 h before it.
    private static List<ComplianceWorkInterval> Work() =>
    [
        Iv(Sat14, 10, 0, 20, 30),
        Iv(Sun15, 8, 0, 14, 0),
        Iv(Sun15, 16, 0, 20, 30),
        Iv(Mon16, 8, 0, 14, 0),
    ];

    private static List<TimeEntry> Entries() =>
    [
        Entry(Sat14, 10.5m),
        Entry(Sun15, 10.5m),
        Entry(Mon16, 6.0m),
    ];

    private static TimeEntry Entry(DateOnly date, decimal hours) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        AgreementCode = "AC",
        OkVersion = "OK24",
    };

    private static List<ComplianceConfigRun> Runs(decimal before16, decimal from16) =>
        EngineConfigRequests.ComplianceRuns("AC", March1, March31, null,
            (day, compiled) => EngineConfigRequests.With(
                compiled, nameof(AgreementRuleConfig.MinimumRestHours), day < Mon16 ? before16 : from16));

    private static List<ComplianceViolation> DailyRest(CheckComplianceRequest request)
    {
        var configForDay = RuleRegistry.ComplianceConfigForDay(request);
        var result = RestPeriodRule.Evaluate(
            request.Profile, request.Entries, request.PeriodStart, request.PeriodEnd, configForDay,
            request.WorkIntervals, CopenhagenBusinessDate.Zone);
        return result.Violations.Concat(result.Warnings)
            .Where(f => f.ViolationType == ComplianceViolationType.DAILY_REST)
            .ToList();
    }

    private static CheckComplianceRequest Request(List<ComplianceConfigRun> runs) =>
        EngineConfigRequests.Compliance(Profile(), Entries(), March1, March31, Work(), runs);

    /// <summary>
    /// Kind <b>N</b> (AC-C1j; names the dated-runs form the sprint creates), proved red by mutation
    /// <b>M-4</b> (each judged work end uses the run covering the day before its date), run at the
    /// TASK-14826-T gate in a worktree detached at this pass's tip. Red conditions: under M-4 arm (1)
    /// judges the Monday end by Sunday's 11 h and finds nothing on Monday; arm (2) judges Monday by
    /// Sunday's 12 h and flags Monday, so the "none dated 16 March" assertion fails. Arm (3) is the
    /// coverage guard (S10), not the per-day value, so it still passes under M-4.
    ///
    /// Expected values from the spec (RR-7): AC/OK24 has no derogation, so a finding is a VIOLATION;
    /// the 11.5 h rest is below 12 and not below 11. (1) 11 h for 1-15 March, 12 h from 16 March: one
    /// finding, dated Monday 16 March, threshold 12, actual 11.5, none on Sunday 15 March. (2) 12 h then
    /// 11 h: one finding on Sunday 15 March (the day's lowest of its two ends, both 11.5), none on 16
    /// March. (3) one run covering 1-15 March only: the work on 16 March has no run, refused with S10.
    /// </summary>
    [Fact]
    public void WorkEndOnMonday_JudgedByMondaysValue_BothDirections()
    {
        // (1) 11 h until Sunday, 12 h from Monday
        var up = DailyRest(Request(Runs(11.0m, 12.0m)));
        var mondayFinding = Assert.Single(up);
        Assert.Equal(Mon16, mondayFinding.Date);
        Assert.Equal(12.0m, mondayFinding.ThresholdValue);
        Assert.Equal(11.5m, mondayFinding.ActualValue);
        Assert.Equal(ComplianceSeverity.VIOLATION, mondayFinding.Severity);
        Assert.DoesNotContain(up, f => f.Date == Sun15);

        // (2) 12 h until Sunday, 11 h from Monday
        var down = DailyRest(Request(Runs(12.0m, 11.0m)));
        var sundayFinding = Assert.Single(down);
        Assert.Equal(Sun15, sundayFinding.Date);
        Assert.Equal(12.0m, sundayFinding.ThresholdValue);
        Assert.Equal(11.5m, sundayFinding.ActualValue);
        Assert.DoesNotContain(down, f => f.Date == Mon16);

        // (3) one run for 1-15 March only: the Monday work has no config run
        var uncovered = Request(EngineConfigRequests.ComplianceRuns("AC", March1, Sun15));
        var refused = Assert.Throws<AgreementConfigRefusedException>(() => RuleRegistry.ComplianceConfigForDay(uncovered));
        Assert.Equal(S10, refused.Message);
    }
}
