using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S145 / TASK-14514 — "screen parity" (refinement Stage A criterion :629): what the employee SEES as the week's
/// norm on the screen equals what payroll JUDGES the week against.
///
/// <para>
/// <b>Plain language.</b> The Skema screen shows a per-day norm (<c>DailyNormCalculator</c>); the payslip rules
/// compare a week's hours with a week norm. Before S145 the screen added five rounded days (30.00 h for a
/// 30-hour employee) while payroll multiplied the week (37 x 0.811 = 30.007 h): the employee saw one figure and
/// was paid against another. Both now go through ONE helper (<c>DayNorm</c>), so the week norm the engine uses
/// equals the sum of the screen's daily values. A 30-hour contract is stored as 0.811 (NUMERIC(4,3)), so a
/// weekday is Round(37 x 0.811 / 5, 2) = Round(6.0014, 2) = <b>6.00</b> and a Monday-Sunday week is <b>30.00</b>
/// (weekends carry 0).
/// </para>
///
/// <para>
/// <b>Cross-assembly.</b> The left side is the Backend's <c>DailyNormCalculator</c> (the real class, with the real
/// <c>ConfigResolutionService</c> and no local agreement profile; its repositories point at an unreachable
/// connection, so the resolver falls back to the static central configuration, as its own tests do). The right
/// side is the Rule Engine's <c>WeeklyTimelineCore</c>: one piece Monday 7 - Sunday 13 September 2026 (no public
/// holiday) at fraction 0.811. Both are asserted against the hand-derived 30.00 as well as against each other, so
/// the two cannot drift together.
/// </para>
///
/// <para>
/// <b>Red conditions</b> (RED by MUTATION at the A.4 gate; <c>docs/AGENTS.md</c> § "RED evidence"): MU-31 (the
/// core's week norm = <c>weekly x fraction</c> instead of the per-day sum) gives 30.007 and trips the rule-side
/// 30.00 assertion and the parity assertion. The fact compiles and passes on the merged A.1 core today.
/// </para>
/// </summary>
public sealed class ScreenParityTests
{
    private static readonly DateOnly Monday = new(2026, 9, 7);

    [Fact]
    public async Task Fraction0811_RuleWeekNorm3000_EqualsSumOfDailyNormCalculator()
    {
        var profile = new EmploymentProfile
        {
            EmployeeId = "EMP-S145-PARITY",
            AgreementCode = "HK",
            OkVersion = "OK26",
            EmploymentCategory = "Standard",
            PartTimeFraction = 0.811m,
        };

        // The screen: the per-day norms of the real calculator, Monday-Sunday.
        var screen = CreateScreenCalculator();
        var screenDays = new List<decimal>();
        for (var i = 0; i < 7; i++)
        {
            var norm = await screen.ComputeNormForProfileAsync(profile, Monday.AddDays(i), "ORG-PARITY");
            Assert.NotNull(norm);
            screenDays.Add(norm!.Value);
        }
        var screenWeek = screenDays.Sum();

        // The rule: the core's norm for the same Monday-Sunday week, one piece at fraction 0.811.
        var hk26 = CentralAgreementConfigs.GetConfig("HK", "OK26");
        var entries = Enumerable.Range(0, 5).Select(i => new TimeEntry
        {
            EmployeeId = profile.EmployeeId,
            Date = Monday.AddDays(i),
            Hours = 6.00m,
            AgreementCode = "HK",
            OkVersion = "OK26",
        }).ToList();
        var core = WeeklyTimelineCore.Evaluate(new TimelineCoreInput(
            profile.EmployeeId, Monday, Monday.AddDays(6), null,
            [new ResolvedPiece(Monday, Monday.AddDays(6), 0.811m, hk26)],
            entries, Array.Empty<AbsenceEntry>(), 0m));
        var ruleWeekNorm = core.Norm.NormHoursTotal;

        Assert.Multiple(
            // Five weekdays at 6.00 and a zero weekend: the screen's own figures.
            () => Assert.Equal(new[] { 6.00m, 6.00m, 6.00m, 6.00m, 6.00m, 0m, 0m }, screenDays),
            () => Assert.Equal(30.00m, screenWeek),
            // The rule's week norm: 30.00, not the week-level 37 x 0.811 = 30.007.
            () => Assert.Equal(30.00m, ruleWeekNorm),
            // And the parity itself.
            () => Assert.Equal(screenWeek, ruleWeekNorm));
    }

    private static DailyNormCalculator CreateScreenCalculator()
    {
        // Unreachable connection: nothing here opens one that matters. ResolveAsync catches the failed DB read
        // and falls back to CentralAgreementConfigs; no local agreement profile repository is wired (4-arg ctor).
        const string unreachable = "Host=localhost;Port=1;Database=na;Username=na;Password=na;Timeout=1;Command Timeout=1";
        var factory = new DbConnectionFactory(unreachable);
        var resolver = new ConfigResolutionService(
            new AgreementConfigRepository(factory),
            new LocalConfigurationRepository(factory),
            new PositionOverrideRepository(factory),
            NullLogger<ConfigResolutionService>.Instance);
        return new DailyNormCalculator(new NoProfileResolver(), resolver);
    }

    /// <summary>Not used by <c>ComputeNormForProfileAsync</c> (the profile is handed in).</summary>
    private sealed class NoProfileResolver : IEmploymentProfileResolver
    {
        public Task<EmploymentProfile?> GetByEmployeeIdAtAsync(string employeeId, DateOnly asOfDate, CancellationToken ct = default) =>
            Task.FromResult<EmploymentProfile?>(null);
    }
}
