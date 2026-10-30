using System.Reflection;
using StatsTid.RuleEngine.Api.Config;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S147 / TASK-14708-T (QUAL-231, AC-231-2, M-7) — the absence vocabulary and its ONE classification.
///
/// <para>
/// <b>The story, in plain language.</b> Every absence code Skema can store must be listed once in the shared vocabulary
/// (<see cref="AbsenceTypes"/>), and every listed code must be exactly one of three things in one place
/// (<see cref="AbsenceClassification"/>): <i>credit</i> (a paid absence: the hours count as worked, so a whole day leaves
/// flex unchanged), <i>reduction</i> (unpaid leave: the day is not owed) or <i>neutral</i> (moves nothing; unused today).
/// Before S147 two lists drifted apart (the engine spelled the child's sick days one way, Skema stored them another, and
/// <c>SICK_DAY</c> was in none), which made payroll charge a sick day against flex while the screen showed it as neutral.
/// </para>
///
/// <para>
/// <b>Expected values come from the spec</b>, written out below as literals (the STORED spellings, which is the point:
/// they are not read from <see cref="AbsenceTypes"/>): Statens arbejdstidsaftale § 7 nr. 2 and its remark — days with a
/// right to paid absence (sick days, holiday, leave with pay, ...) count with the hours the employee should have worked;
/// unpaid leave does not. The sick day is credit for HK and PROSA by that text and for AC-agreement staff as a labelled
/// default by analogy (owner ruling D-8b = (a)).
/// </para>
/// </summary>
public class AbsenceClassificationTests
{
    private static readonly DateOnly Monday = new(2026, 6, 8);

    /// <summary>The accepted vocabulary: exactly what the Skema save stores in <c>absences_projection.absence_type</c>.</summary>
    private static readonly string[] AcceptedVocabulary =
    {
        "SICK_DAY", "VACATION", "CARE_DAY", "CHILD_SICK_DAY", "CHILD_SICK_DAY_2", "CHILD_SICK_DAY_3",
        "PARENTAL_LEAVE", "SENIOR_DAY", "SPECIAL_HOLIDAY_ALLOWANCE", "LEAVE_WITH_PAY", "LEAVE_WITHOUT_PAY",
    };

    private static string[] ReflectedVocabulary() =>
        typeof(AbsenceTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

    /// <summary>
    /// AC-231-2 (kind F: this fact names only <see cref="AbsenceTypes"/>, which existed before S147). Reflection over
    /// <see cref="AbsenceTypes"/> yields exactly the accepted set: eleven stored spellings, no more, no fewer, none repeated.
    ///
    /// <para><b>Red condition (F, against <c>991a5890</c>).</b> The vocabulary there had ten codes, spelled
    /// <c>CHILD_SICK_1/2/3</c> and without <c>SICK_DAY</c> (<c>AbsenceEntry.cs:15-24</c>), so both assertions fail. M-7 does not
    /// redden this fact (it drops a classification entry, not a vocabulary code). This is the vocabulary's own pin: it
    /// fails if a code is added, dropped or respelled. (Relabelled N → F by the Orchestrator on Codex 5a NOTE 1.)</para>
    /// </summary>
    [Fact]
    public void AbsenceTypes_ReflectionEqualsTheAcceptedStoredVocabulary()
    {
        var reflected = ReflectedVocabulary();

        Assert.Equal(AcceptedVocabulary.Length, reflected.Length);
        Assert.Equal(
            AcceptedVocabulary.OrderBy(c => c, StringComparer.Ordinal).ToArray(),
            reflected.OrderBy(c => c, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// AC-231-2 (kind N). Every vocabulary code appears in <see cref="AbsenceClassification"/> exactly ONCE, and the
    /// classification holds no code the vocabulary does not list: a code with two effects, or none, or an extra one, fails.
    ///
    /// <para><b>Red conditions (N; mutation M-7, per the plan's P-14: drop <c>SickDay</c> from
    /// <c>AbsenceClassification.InDeclarationOrder</c>, keeping the API).</b> With the <c>SICK_DAY</c> row removed, its
    /// occurrence count is 0, not 1, so <c>Assert.Equal(1, count)</c> fails for <c>SICK_DAY</c> (and the entry-count
    /// assertion reads 10, not 11).</para>
    /// </summary>
    [Fact]
    public void EveryVocabularyCode_HasExactlyOneClassification()
    {
        var entries = AbsenceClassification.InDeclarationOrder;

        Assert.Equal(AcceptedVocabulary.Length, entries.Count);
        Assert.Multiple(AcceptedVocabulary.Select(code =>
            (Action)(() => Assert.Equal(1, entries.Count(e => e.Key == code)))).ToArray());
        Assert.Multiple(AcceptedVocabulary.Select(code =>
            (Action)(() => Assert.True(AbsenceClassification.ByCode.ContainsKey(code), $"{code} has no classification"))).ToArray());
    }

    /// <summary>
    /// AC-231-2 / AC-231-8 (kind N). <c>SICK_DAY</c> is classified <b>credit</b>: for HK and PROSA by Statens
    /// arbejdstidsaftale § 7 nr. 2 (its remark names sick days), for AC-agreement staff as the labelled default by analogy.
    /// The engine's own question agrees: <see cref="AbsenceRule.GrantsNormCredit"/> says yes and
    /// <see cref="AbsenceRule.ReducesNorm"/> says no.
    ///
    /// <para><b>Red conditions (N; mutation M-7).</b> With the <c>SICK_DAY</c> row dropped, <c>TryGetEffect</c> finds
    /// nothing: <c>IsCredit("SICK_DAY")</c> is false and <c>GrantsNormCredit("SICK_DAY")</c> is false.</para>
    /// </summary>
    [Fact]
    public void SickDay_IsClassifiedCredit()
    {
        Assert.Multiple(
            () => Assert.True(AbsenceClassification.TryGetEffect("SICK_DAY", out var effect) && effect == AbsenceNormEffect.Credit),
            () => Assert.True(AbsenceClassification.IsCredit("SICK_DAY")),
            () => Assert.False(AbsenceClassification.IsReduction("SICK_DAY")),
            () => Assert.True(AbsenceRule.GrantsNormCredit("SICK_DAY")),
            () => Assert.False(AbsenceRule.ReducesNorm("SICK_DAY")));
    }

    /// <summary>
    /// AC-231-2 (kind N). The whole classification table, from the spec: ten credit codes (the paid absences, § 7 nr. 2:
    /// own sick day, holiday, omsorgsdag, the child's 1st, 2nd and 3rd sick day, barsel, seniordag, særlig feriedag,
    /// tjenestefrihed med løn) and ONE reduction (<c>LEAVE_WITHOUT_PAY</c>, unpaid). No code is neutral. The engine's two
    /// questions are a projection of the same table (one source, never a second list).
    ///
    /// <para><b>Red conditions (N; mutation M-7).</b> The <c>SICK_DAY</c> row (<c>credit</c>) fails with "no classification";
    /// any other row fails if its effect is flipped.</para>
    /// </summary>
    [Theory]
    [InlineData("SICK_DAY", AbsenceNormEffect.Credit)]
    [InlineData("VACATION", AbsenceNormEffect.Credit)]
    [InlineData("CARE_DAY", AbsenceNormEffect.Credit)]
    [InlineData("CHILD_SICK_DAY", AbsenceNormEffect.Credit)]
    [InlineData("CHILD_SICK_DAY_2", AbsenceNormEffect.Credit)]
    [InlineData("CHILD_SICK_DAY_3", AbsenceNormEffect.Credit)]
    [InlineData("PARENTAL_LEAVE", AbsenceNormEffect.Credit)]
    [InlineData("SENIOR_DAY", AbsenceNormEffect.Credit)]
    [InlineData("SPECIAL_HOLIDAY_ALLOWANCE", AbsenceNormEffect.Credit)]
    [InlineData("LEAVE_WITH_PAY", AbsenceNormEffect.Credit)]
    [InlineData("LEAVE_WITHOUT_PAY", AbsenceNormEffect.Reduction)]
    public void EveryCode_HasTheSpecifiedEffect_AndTheEngineReadsTheSameTable(string code, AbsenceNormEffect expected)
    {
        Assert.True(AbsenceClassification.TryGetEffect(code, out var actual), $"{code} has no classification");
        Assert.Multiple(
            () => Assert.Equal(expected, actual),
            () => Assert.Equal(expected == AbsenceNormEffect.Credit, AbsenceRule.GrantsNormCredit(code)),
            () => Assert.Equal(expected == AbsenceNormEffect.Reduction, AbsenceRule.ReducesNorm(code)));
    }

    /// <summary>
    /// AC-231-2 (kind N, "boundary pin — holds today"). A code outside the vocabulary is unknown and is neither credit nor
    /// reduction: the old numbered child spellings (which nothing ever stored), a lower-case spelling (stored codes are
    /// case-exact), a made-up code, the empty string and null.
    ///
    /// <para><b>Red conditions (N).</b> A classification that matched loosely (case-insensitive, or the old spellings kept
    /// as aliases) fails on the corresponding row: <c>IsKnown</c> would answer true.</para>
    /// </summary>
    [Theory]
    [InlineData("CHILD_SICK_1")]
    [InlineData("CHILD_SICK_2")]
    [InlineData("CHILD_SICK_3")]
    [InlineData("sick_day")]
    [InlineData("SICK")]
    [InlineData("NOT_A_CODE")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownCode_IsNotKnown_NeitherCreditNorReduction(string? code)
    {
        Assert.Multiple(
            () => Assert.False(AbsenceClassification.IsKnown(code)),
            () => Assert.False(AbsenceClassification.IsCredit(code)),
            () => Assert.False(AbsenceClassification.IsReduction(code)),
            () => Assert.False(AbsenceClassification.TryGetEffect(code, out _)));
    }

    /// <summary>
    /// AC-231-6 (kind R, a regression pin: an existing behaviour kept). The ABSENCE LINES themselves are unchanged by the
    /// S147 re-spelling: for every stored code, <see cref="AbsenceRule.Evaluate"/> yields ONE line whose time type is the
    /// stored code itself (the payroll wage-type mapping keys on it: <c>SICK_DAY</c> to SLS_0540, <c>CHILD_SICK_DAY</c> to
    /// SLS_0530), with the registered hours, rate 1.0 and the absence's date. Golden values, written out from the spec of
    /// the line (code in, same code out), not read from the rule.
    ///
    /// <para><b>Red conditions (R).</b> Holds on the pre-S147 code for every stored spelling (the old switch mapped each
    /// stored code to itself); it fails if the re-spelling changed a line's time type, hours, rate or date.</para>
    /// </summary>
    [Theory]
    [InlineData("SICK_DAY")]
    [InlineData("VACATION")]
    [InlineData("CARE_DAY")]
    [InlineData("CHILD_SICK_DAY")]
    [InlineData("CHILD_SICK_DAY_2")]
    [InlineData("CHILD_SICK_DAY_3")]
    [InlineData("PARENTAL_LEAVE")]
    [InlineData("SENIOR_DAY")]
    [InlineData("SPECIAL_HOLIDAY_ALLOWANCE")]
    [InlineData("LEAVE_WITH_PAY")]
    [InlineData("LEAVE_WITHOUT_PAY")]
    public void AbsenceLine_ForEveryStoredCode_IsTheCodeItself_GoldenLine(string storedCode)
    {
        var profile = new EmploymentProfile
        {
            EmployeeId = "EMP-S147",
            AgreementCode = "HK",
            OkVersion = "OK26",
            EmploymentCategory = "Standard",
            PartTimeFraction = 1.0m,
        };
        var absences = new List<AbsenceEntry>
        {
            new()
            {
                EmployeeId = "EMP-S147", Date = Monday, AbsenceType = storedCode, Hours = 7.4m,
                AgreementCode = "HK", OkVersion = "OK26",
            },
        };

        var result = AbsenceRule.Evaluate(profile, absences, Monday, Monday.AddDays(6));

        Assert.True(result.Success);
        var line = Assert.Single(result.LineItems);
        Assert.Multiple(
            () => Assert.Equal(storedCode, line.TimeType),
            () => Assert.Equal(7.4m, line.Hours),
            () => Assert.Equal(1.0m, line.Rate),
            () => Assert.Equal(Monday, line.Date));
    }
}

/// <summary>
/// S147 / TASK-14708-T (QUAL-231, AC-231-1 and AC-231-8 at the engine) — what the REAL weekly core
/// (<see cref="WeeklyTimelineCore"/>) does with the STORED sick codes. These facts name only the stored spellings as
/// string literals and types that exist on today's master, so they run against the pre-S147 code too (that is how their
/// kind-F RED is shown: see each fact).
///
/// <para>
/// <b>Expected values come from the spec</b>: Statens arbejdstidsaftale § 7 nr. 2 (a paid sick day counts with the hours
/// the employee should have worked that day, 7.4 for a full-timer; the research's partial-day reading RR-3: hours worked
/// plus the day's remaining hours registered as sick close the day at its norm), the flex formula
/// <c>worked + credit - norm</c> clamped at the agreement's cap (HK 100 h), and the Skema screen's day rule
/// <c>worked + absence - norm</c>. AC-agreement credit is the labelled default by analogy (owner ruling D-8b = (a)).
/// </para>
/// </summary>
public class SickCodeCreditThroughEngineTests
{
    // Monday 8 June 2026 to Sunday 14 June 2026: one Monday-Sunday week, full-time, norm 37.00.
    private static readonly DateOnly Monday = new(2026, 6, 8);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    private static TimeEntry Worked(DateOnly date, decimal hours, string agreement) => new()
    {
        EmployeeId = "EMP-S147", Date = date, Hours = hours, AgreementCode = agreement, OkVersion = "OK26",
    };

    private static AbsenceEntry Absent(DateOnly date, string storedCode, decimal hours, string agreement) => new()
    {
        EmployeeId = "EMP-S147", Date = date, AbsenceType = storedCode, Hours = hours,
        AgreementCode = agreement, OkVersion = "OK26",
    };

    private static TimelineCoreResult Run(
        string agreement, decimal opening, IReadOnlyList<TimeEntry> entries, IReadOnlyList<AbsenceEntry> absences)
    {
        var config = AgreementConfigProvider.GetConfig(agreement, "OK26");
        return WeeklyTimelineCore.Evaluate(new TimelineCoreInput(
            "EMP-S147", Monday, Sunday, Sunday,
            [new ResolvedPiece(Monday, Sunday, 1.0m, config)],
            entries, absences, opening));
    }

    /// <summary>
    /// AC-231-1 / AC-231-8 at the engine (kind F). Monday to Thursday 7.40 h worked, Friday a stored sick code of 7.40 h:
    /// the week's flex delta is exactly <b>0.00</b> and the balance stays 0.00 (opening 0.00), for each of HK, PROSA and
    /// AC (the AC credit is the labelled default), and for <c>SICK_DAY</c> and each stored child code. The credited hours
    /// are 7.40 and the week's norm 37.00.
    ///
    /// <para><b>Red conditions (F; run on the pre-S147 code, the implementer's base <c>991a5890</c>).</b> The engine's credit
    /// list lacked <c>SICK_DAY</c> and spelled the child codes <c>CHILD_SICK_1/2/3</c>: no credit, so
    /// <c>Delta == -7.40</c> and <c>NewBalance == -7.40</c>, failing <c>Delta == 0.00</c>, for every row.</para>
    /// </summary>
    [Theory]
    [InlineData("HK", "SICK_DAY")]
    [InlineData("PROSA", "SICK_DAY")]
    [InlineData("AC", "SICK_DAY")]
    [InlineData("HK", "CHILD_SICK_DAY")]
    [InlineData("PROSA", "CHILD_SICK_DAY")]
    [InlineData("AC", "CHILD_SICK_DAY")]
    [InlineData("HK", "CHILD_SICK_DAY_2")]
    [InlineData("PROSA", "CHILD_SICK_DAY_2")]
    [InlineData("PROSA", "CHILD_SICK_DAY_3")]
    public void StoredSickCode_WholeFriday_CreditsTheDay_WeekDeltaZero(string agreement, string storedCode)
    {
        var entries = Enumerable.Range(0, 4).Select(i => Worked(Monday.AddDays(i), 7.4m, agreement)).ToList();
        var absences = new List<AbsenceEntry> { Absent(Monday.AddDays(4), storedCode, 7.4m, agreement) };

        var flex = Run(agreement, opening: 0m, entries, absences).Flex;

        Assert.Multiple(
            () => Assert.Equal(7.4m, flex.AbsenceNormCredits),
            () => Assert.Equal(37.00m, flex.NormHours),
            () => Assert.Equal(0.00m, flex.Delta),
            () => Assert.Equal(0.00m, flex.NewBalance));
    }

    /// <summary>
    /// RR-3 at the engine (kind F). A PARTIAL sick day: Monday to Thursday 7.40 h worked, Friday 3.00 h worked plus 4.40 h
    /// of stored <c>SICK_DAY</c>. The day closes at its norm (3.00 + 4.40 = 7.40), so the week's delta is exactly
    /// <b>0.00</b>, for HK, PROSA and AC. The over-credit case (worked plus sick above the norm) is a QUAL-231 residual
    /// for S148 and is deliberately NOT pinned.
    ///
    /// <para><b>Red conditions (F; pre-S147 code).</b> The 4.40 h were not credited: <c>Delta == -4.40</c>, failing
    /// <c>Delta == 0.00</c>.</para>
    /// </summary>
    [Theory]
    [InlineData("HK")]
    [InlineData("PROSA")]
    [InlineData("AC")]
    public void PartialSickDay_WorkedPlusRemainingSickHours_DayClosesAtItsNorm_WeekDeltaZero(string agreement)
    {
        var entries = Enumerable.Range(0, 4).Select(i => Worked(Monday.AddDays(i), 7.4m, agreement)).ToList();
        entries.Add(Worked(Monday.AddDays(4), 3.0m, agreement));
        var absences = new List<AbsenceEntry> { Absent(Monday.AddDays(4), "SICK_DAY", 4.4m, agreement) };

        var flex = Run(agreement, opening: 0m, entries, absences).Flex;

        Assert.Multiple(
            () => Assert.Equal(4.4m, flex.AbsenceNormCredits),
            () => Assert.Equal(0.00m, flex.Delta),
            () => Assert.Equal(0.00m, flex.NewBalance));
    }

    /// <summary>
    /// AC-231-7 at the engine (kind F). The credit can carry the balance over the cap. HK opens at 98.00 h (cap 100 h); the
    /// week has 12.40 h worked on Monday, 7.40 h on each of Tuesday, Wednesday and Thursday, and a stored sick day of
    /// 7.40 h on Friday. Flex moves 12.40 + 7.40 + 7.40 + 7.40 + 7.40 - 37.00 = <b>+5.00</b>; raw balance 103.00; the cap
    /// holds the balance at <b>100.00</b> and <b>3.00</b> is paid out as one <c>FLEX_PAYOUT</c> line. (The week's worked
    /// hours are 34.60, under the 37 h weekly norm, so no overtime is involved.)
    ///
    /// <para><b>Red conditions (F; pre-S147 code).</b> Without the credit the movement is 5.00 - 7.40 = -2.40: balance
    /// <b>95.60</b>, excess 0.00 and no payout line, failing <c>NewBalance == 100.00</c>, <c>ExcessForPayout == 3.00</c>
    /// and the payout line.</para>
    /// </summary>
    [Fact]
    public void StoredSickDay_CreditCrossesTheHkCap_ExcessIsPaidOutAsFlexPayout()
    {
        var entries = new List<TimeEntry>
        {
            Worked(Monday, 12.4m, "HK"),
            Worked(Monday.AddDays(1), 7.4m, "HK"),
            Worked(Monday.AddDays(2), 7.4m, "HK"),
            Worked(Monday.AddDays(3), 7.4m, "HK"),
        };
        var absences = new List<AbsenceEntry> { Absent(Monday.AddDays(4), "SICK_DAY", 7.4m, "HK") };

        var result = Run("HK", opening: 98.0m, entries, absences);

        Assert.Multiple(
            () => Assert.Equal(5.00m, result.Flex.Delta),
            () => Assert.Equal(100.00m, result.Flex.NewBalance),
            () => Assert.Equal(3.00m, result.Flex.ExcessForPayout),
            () => Assert.Equal("FLEX_PAYOUT", result.FlexPayout?.TimeType),
            () => Assert.Equal(3.00m, result.FlexPayout?.Hours));
    }

    /// <summary>
    /// Control (kind R, a regression pin: an existing behaviour kept). A paid holiday day (<c>VACATION</c>) was credited
    /// before S147 and still is: the same week as the first fact with <c>VACATION</c> on Friday has a delta of 0.00. It
    /// shows the credit assertions above are about the sick codes, not about any absence on Friday.
    ///
    /// <para><b>Red conditions (R).</b> Holds on the pre-S147 code; fails if the classification rework stops crediting
    /// <c>VACATION</c>.</para>
    /// </summary>
    [Fact]
    public void Vacation_StillCredited_WeekDeltaZero_Control()
    {
        var entries = Enumerable.Range(0, 4).Select(i => Worked(Monday.AddDays(i), 7.4m, "HK")).ToList();
        var absences = new List<AbsenceEntry> { Absent(Monday.AddDays(4), "VACATION", 7.4m, "HK") };

        var flex = Run("HK", opening: 0m, entries, absences).Flex;

        Assert.Equal(0.00m, flex.Delta);
    }
}
