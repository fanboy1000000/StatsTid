using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Shared;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S148 TASK-14826-T, AC-C1e (the engine half) - the engine computes with the agreement config the
/// request hands it, not with the compiled table.
///
/// <para>
/// <b>Plain language.</b> Since ADR-043 D12 the caller (payroll, the Backend) resolves the agreement
/// rules in force and sends them; the engine must use exactly those. The proof is a deliberately
/// unusual config: HK's real OK26 rules, but a 32.0-hour week instead of 37.0. If the engine used its
/// own table the weekly norm would still read 37.00; if it uses what it is handed it reads 32.00.
/// </para>
///
/// <para>
/// Expected values come from the spec (a full-time week's norm is the config's
/// <c>WeeklyNormHours</c> times the fraction), not from observing the engine.
/// </para>
/// </summary>
public class EngineConfigFromRequestTests
{
    private static readonly DateOnly Monday = new(2026, 9, 14);
    private static readonly DateOnly Sunday = new(2026, 9, 20);

    private static EmploymentProfile Profile() => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = "HK",
        OkVersion = "OK26",
        EmploymentCategory = "STANDARD",
    };

    private static List<TimeEntry> Week() =>
        Enumerable.Range(0, 5).Select(i => new TimeEntry
        {
            EmployeeId = "EMP001",
            Date = Monday.AddDays(i),
            Hours = 7.40m,
            AgreementCode = "HK",
            OkVersion = "OK26",
        }).ToList();

    /// <summary>
    /// Kind <b>N</b> (AC-C1e; names the config parameter the sprint adds), proved red by mutation
    /// <b>M-2</b> (the registry's time-rule dispatch ignores the config it is handed and reads the
    /// compiled table), run at the TASK-14826-T gate in a worktree detached at this pass's tip.
    /// Red conditions: under M-2 the norm is HK/OK26's compiled 37.00, so the assertion of 32.00
    /// fails.
    /// </summary>
    [Fact]
    public void Evaluate_UsesTheRequestConfig()
    {
        var config = EngineConfigRequests.With(
            EngineConfigRequests.Config("HK", "OK26"), nameof(AgreementRuleConfig.WeeklyNormHours), 32.0m);

        var result = new RuleRegistry().Evaluate("NORM_CHECK_37H", Profile(), Week(), Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(32.00m, result.NormHoursTotal);
    }

    /// <summary>
    /// Kind <b>N</b> (AC-C1e), proved red by mutation <b>M-3</b> (<c>EvaluateTimeline</c> ignores each
    /// piece's config and reads the compiled table), run at the TASK-14826-T gate in a worktree
    /// detached at this pass's tip. Red conditions: under M-3 the <c>NORM_CHECK_37H</c> row reads
    /// 37.00, so the 32.00 assertion fails. The same request with the unmodified compiled config
    /// reads 37.00, so the answer follows the config handed in (REF:820).
    /// </summary>
    [Fact]
    public void EvaluateTimeline_UsesTheRequestConfig()
    {
        var compiled = EngineConfigRequests.Config("HK", "OK26");
        var config = EngineConfigRequests.With(compiled, nameof(AgreementRuleConfig.WeeklyNormHours), 32.0m);

        var changed = new RuleRegistry().EvaluateTimeline(EngineConfigRequests.Timeline(
            "EMP001", Monday, Sunday,
            [EngineConfigRequests.Piece(Monday, Sunday, 1.0m, "HK", null, config)], Week()));
        var unchanged = new RuleRegistry().EvaluateTimeline(EngineConfigRequests.Timeline(
            "EMP001", Monday, Sunday,
            [EngineConfigRequests.Piece(Monday, Sunday, 1.0m, "HK", null, compiled)], Week()));

        Assert.Equal("NORM_CHECK_37H", changed.Core.Norm.RuleId);
        Assert.Equal(32.00m, changed.Core.Norm.NormHoursTotal);
        Assert.Equal(37.00m, unchanged.Core.Norm.NormHoursTotal);
    }
}
