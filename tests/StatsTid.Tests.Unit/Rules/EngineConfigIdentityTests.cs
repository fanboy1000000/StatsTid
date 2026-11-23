using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Shared;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S148 TASK-14826-T, AC-C1l - the engine refuses an agreement config that is not its request's own.
///
/// <para>
/// <b>Plain language.</b> A config carries the agreement code and OK version it was resolved for. A
/// piece, profile or compliance run that arrives with a config for another agreement or another OK
/// version is refused with a fixed sentence, never obeyed. The sentences S2, S4, S5 and S9 are the
/// ruled ones of TASK-14826's brief (item 3), quoted here as written.
/// </para>
/// </summary>
public class EngineConfigIdentityTests
{
    private const string S2 = "The agreement configuration names another agreement code or OK version than the profile.";
    private const string S4 = "A piece may not span two OK versions.";
    private const string S5 = "A piece's Config names another agreement code or OK version than the piece.";
    private const string S9 = "A config run's Config names another agreement code or OK version than the run.";

    private static readonly DateOnly Mon6Apr = new(2026, 4, 6);
    private static readonly DateOnly Sun12Apr = new(2026, 4, 12);

    private static EmploymentProfile Profile(string code, string ok) => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = code,
        OkVersion = ok,
        EmploymentCategory = "STANDARD",
    };

    private static List<TimeEntry> Entries(DateOnly from, int days) =>
        Enumerable.Range(0, days).Select(i => new TimeEntry
        {
            EmployeeId = "EMP001",
            Date = from.AddDays(i),
            Hours = 7.40m,
            AgreementCode = "HK",
            OkVersion = "OK26",
        }).ToList();

    private static string Timeline(DateOnly from, DateOnly to, params TimelinePieceRequest[] pieces)
    {
        var request = EngineConfigRequests.Timeline("EMP001", from, to, pieces.ToList(), Entries(from, 5));
        try
        {
            new RuleRegistry().EvaluateTimeline(request);
            return "evaluated";
        }
        catch (TimelineInputException ex)
        {
            return ex.Message;
        }
    }

    private static string ProfileRefusal(EmploymentProfile profile, AgreementRuleConfig config)
    {
        try
        {
            new RuleRegistry().Evaluate("NORM_CHECK_37H", profile, Entries(Mon6Apr, 5), Mon6Apr, Sun12Apr, config);
            return "evaluated";
        }
        catch (AgreementConfigRefusedException ex)
        {
            return ex.Message;
        }
    }

    private static string ComplianceRefusal(List<ComplianceConfigRun> runs)
    {
        var request = EngineConfigRequests.Compliance(
            Profile("HK", "OK24"), new List<TimeEntry>(), new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31),
            [new ComplianceWorkInterval(new DateOnly(2026, 3, 10), new TimeOnly(8, 0), new TimeOnly(16, 0))], runs);
        try
        {
            RuleRegistry.ComplianceConfigForDay(request);
            return "accepted";
        }
        catch (AgreementConfigRefusedException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Kind <b>N</b> (AC-C1l; names the identity check the sprint creates), proved red by mutation
    /// <b>M-5</b> (the identity check dropped: a config is accepted whatever it names), run at the
    /// TASK-14826-T gate in a worktree detached at this pass's tip. Red conditions: under M-5 arms
    /// (1), (2), (3) and (5) see no refusal ("evaluated"/"accepted" instead of S5, S5, S2, S9).
    /// Arm (4) is the OK-version span check (S4), not the identity check, so it still passes under M-5.
    /// The controls (HK's own config for the right OK version is evaluated; the piece cut at 1 April
    /// into two pieces, each with its own version's HK config, is evaluated) keep the refusals from
    /// passing by refusing everything.
    /// </summary>
    [Fact]
    public void OtherAgreementCodeOrOkVersion_Refused()
    {
        var hkOk26 = EngineConfigRequests.Config("HK", "OK26");
        var hkOk24 = EngineConfigRequests.Config("HK", "OK24");
        var acOk26 = EngineConfigRequests.Config("AC", "OK26");
        var acOk24 = EngineConfigRequests.Config("AC", "OK24");

        // (1) a piece of HK, Monday 6 - Sunday 12 April 2026 (OK26), carrying AC's OK26 config
        Assert.Equal(S5, Timeline(Mon6Apr, Sun12Apr, EngineConfigRequests.Piece(Mon6Apr, Sun12Apr, 1.0m, "HK", null, acOk26)));
        // (2) the same week carrying HK's OK24 config
        Assert.Equal(S5, Timeline(Mon6Apr, Sun12Apr, EngineConfigRequests.Piece(Mon6Apr, Sun12Apr, 1.0m, "HK", null, hkOk24)));
        // control for (1) and (2): HK's own OK26 config
        Assert.Equal("evaluated", Timeline(Mon6Apr, Sun12Apr, EngineConfigRequests.Piece(Mon6Apr, Sun12Apr, 1.0m, "HK", null, hkOk26)));

        // (3) the profile arm: an HK/OK26 profile with AC's OK26 config
        Assert.Equal(S2, ProfileRefusal(Profile("HK", "OK26"), acOk26));
        // control: HK's own
        Assert.Equal("evaluated", ProfileRefusal(Profile("HK", "OK26"), hkOk26));

        // (4) one piece Monday 30 March - Sunday 5 April 2026, across 1 April, carrying HK's OK24 config
        var mon30Mar = new DateOnly(2026, 3, 30);
        var sun5Apr = new DateOnly(2026, 4, 5);
        Assert.Equal(S4, Timeline(mon30Mar, sun5Apr, EngineConfigRequests.Piece(mon30Mar, sun5Apr, 1.0m, "HK", null, hkOk24)));
        // control: cut at 1 April, each piece with its own version's HK config
        Assert.Equal("evaluated", Timeline(mon30Mar, sun5Apr,
            EngineConfigRequests.Piece(mon30Mar, new DateOnly(2026, 3, 31), 1.0m, "HK", null, hkOk24),
            EngineConfigRequests.Piece(new DateOnly(2026, 4, 1), sun5Apr, 1.0m, "HK", null, hkOk26)));

        // (5) a compliance run 1-31 March 2026 of HK carrying AC's OK24 config
        var march1 = new DateOnly(2026, 3, 1);
        var march31 = new DateOnly(2026, 3, 31);
        Assert.Equal(S9, ComplianceRefusal([EngineConfigRequests.Run(march1, march31, "HK", acOk24)]));
        // control: HK's own OK24 config
        Assert.Equal("accepted", ComplianceRefusal([EngineConfigRequests.Run(march1, march31, "HK", hkOk24)]));
    }
}
