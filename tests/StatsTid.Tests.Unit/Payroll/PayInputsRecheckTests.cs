using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Shared;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S148 / TASK-14831-T (AC-PIL-f; refinement B-2) - the pay-inputs recheck notices every difference between what a
/// calculation used and what the derivation gives again inside the recording's turn on the lock, and lets equal inputs
/// through. Locally runnable (no database, no Docker).
///
/// <para>
/// <b>In plain language.</b> Payroll calculates a month, then records it. Between the two, someone may have dated a
/// change into that month (a new agreement version, another institution, a different part-time fraction, a day of
/// employment added or removed). The recording's last step is to work the month's inputs out again and compare them
/// with what the calculation used. These facts feed that comparison inputs that differ in exactly one thing, a month
/// the new version splits in two, and a day the fresh derivation cannot account for; every one must be refused, and
/// equal inputs must pass. The refusal is the sentence the caller sees as 409 <c>payroll-inputs-changed</c>, naming
/// the month and nothing else.
/// </para>
///
/// <para>
/// <b>Kind: N (names <c>EnsurePayInputsUnchanged</c> and <c>RecheckPayInputsUnderLockAsync</c>, which TASK-14831 creates).</b>
/// RED by two named mutations at the pass's gate, never by reverting. <b>M-7</b> (the comparison compares nothing:
/// <c>EnsurePayInputsUnchanged</c> returns before it compares) must trip arms (1) and (2) and nothing else: arm (3)
/// goes through the derivation wrapper, which M-7 does not touch, and arm (4) is the control that passes under both.
/// <b>M-52</b> (the wrapper lets a derivation refusal pass: the catch that turns a day the fresh derivation cannot cover
/// into the new exception returns instead) must trip arm (3) and the two wrapper facts below (no configuration in force,
/// a split refusal); arms (1), (2) and (4) never reach that catch. <b>M-53</b> (the wrapper no longer wraps a failed
/// configuration read: the <c>ConfigLookupUnavailableException</c> catch is removed) must trip the configuration-read fact
/// below and nothing else.
/// Expected values here are the brief's and the plan's: the kind, the month and the two sentences are literals, never read
/// back from the production constants.
/// </para>
/// </summary>
public sealed class PayInputsRecheckTests
{
    private const string EmployeeId = "EMP-S148-PIL-F";
    private const string ExportSentence =
        "The inputs this calculation used changed while it was being recorded; calculate the month again. " +
        "Nothing was exported and no export record was written.";
    private const string CorrectionSentence =
        "The inputs this correction used changed while it was being recorded; run the correction again. " +
        "Nothing was corrected and the export record is unchanged.";

    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar15 = new(2026, 3, 15);
    private static readonly DateOnly Mar16 = new(2026, 3, 16); // a Monday: the day a version changing the weekly norm starts
    private static readonly DateOnly Mar30 = new(2026, 3, 30);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    // ─── the inputs: one run for March on HK OK24 (37 hours), in STY01, whole-time, no position ───

    /// <summary>HK OK24 (37 hours), as a NEW instance on every call: the compiled config is cached and shared, so a plain
    /// lookup would hand both sides the same object and the equal-inputs arm could not tell value from reference equality.</summary>
    private static AgreementRuleConfig Hk37() =>
        EngineConfigRequests.With(EngineConfigRequests.Config("HK", "OK24"), "WeeklyNormHours", 37m);

    private static FrozenPiece Piece(
        DateOnly from, DateOnly to, decimal fraction = 1.0m, string agreementCode = "HK", string? position = null, string org = "STY01") =>
        new(from, to, fraction, agreementCode, position) { OrgId = org };

    private static FrozenConfigRun Run(
        DateOnly from, DateOnly to, AgreementRuleConfig config, string agreementCode = "HK", string? position = null, string org = "STY01") =>
        new(from, to, agreementCode, "OK24", position, org, config);

    private static PlanInputs Inputs(
        IReadOnlyList<FrozenPiece> pieces, IReadOnlyList<FrozenConfigRun> runs, DateOnly? cutoff = null) =>
        new(
            Pieces: pieces,
            SettlementCutoff: cutoff,
            OpeningFlexBalance: 0m,
            EntryEventIds: Array.Empty<Guid>(),
            AbsenceEventIds: Array.Empty<Guid>(),
            AsOfOutboxId: 1,
            EntriesDigest: new string('a', 64),
            AbsencesDigest: new string('b', 64),
            EvaluationModel: "weekly-timeline-v3")
        {
            ConfigRuns = runs,
        };

    /// <summary>What the calculation froze: all of March in one piece and one run.</summary>
    private static PlanInputs Frozen() => Inputs(
        new[] { Piece(Mar01, Mar31) },
        new[] { Run(Mar01, Mar31, Hk37()) });

    private static void AssertRefused(PayInputsChangedException ex, string sentence = ExportSentence)
    {
        Assert.Equal("payroll-inputs-changed", PayInputsChangedException.KindValue);
        Assert.Equal(Mar01, ex.Month);
        Assert.Equal("2026-03", ex.MonthText);
        Assert.Equal(sentence, ex.ErrorSentence);
    }

    // ─── (1) one difference at a time ─────────────────────────────────────

    /// <summary>The fresh derivation, differing from <see cref="Frozen"/> in exactly the named thing.</summary>
    private static PlanInputs FreshWithOneDifference(string difference) => difference switch
    {
        "config-value" => Inputs(
            new[] { Piece(Mar01, Mar31) },
            new[] { Run(Mar01, Mar31, EngineConfigRequests.With(Hk37(), "WeeklyNormHours", 32m)) }),
        "organisation" => Inputs(
            new[] { Piece(Mar01, Mar31, org: "STY02") },
            new[] { Run(Mar01, Mar31, Hk37()) }),
        "fraction" => Inputs(
            new[] { Piece(Mar01, Mar31, fraction: 0.8m) },
            new[] { Run(Mar01, Mar31, Hk37()) }),
        "position" => Inputs(
            new[] { Piece(Mar01, Mar31, position: "SPECIALIST") },
            new[] { Run(Mar01, Mar31, Hk37()) }),
        "agreement-code-on-one-day" => Inputs(
            new[]
            {
                Piece(Mar01, new DateOnly(2026, 3, 17)),
                Piece(new DateOnly(2026, 3, 18), new DateOnly(2026, 3, 18), agreementCode: "AC"),
                Piece(new DateOnly(2026, 3, 19), Mar31),
            },
            new[]
            {
                Run(Mar01, new DateOnly(2026, 3, 17), Hk37()),
                Run(new DateOnly(2026, 3, 18), new DateOnly(2026, 3, 18), EngineConfigRequests.Config("AC", "OK24"), agreementCode: "AC"),
                Run(new DateOnly(2026, 3, 19), Mar31, Hk37()),
            }),
        "employed-day-added" => Inputs(
            new[] { Piece(Mar01, Mar31) },
            new[] { Run(Mar01, Mar31, Hk37()) }),
        "employed-day-removed" => Inputs(
            new[] { Piece(Mar01, Mar30) },
            new[] { Run(Mar01, Mar30, Hk37()) }),
        "settlement-cutoff" => Inputs(
            new[] { Piece(Mar01, Mar31) },
            new[] { Run(Mar01, Mar31, Hk37()) },
            cutoff: new DateOnly(2026, 3, 20)),
        _ => throw new ArgumentOutOfRangeException(nameof(difference), difference, null),
    };

    /// <summary>The frozen inputs for a difference: only "employed-day-added" starts from a month one day short.</summary>
    private static PlanInputs FrozenFor(string difference) => difference == "employed-day-added"
        ? Inputs(new[] { Piece(Mar01, Mar30) }, new[] { Run(Mar01, Mar30, Hk37()) })
        : Frozen();

    /// <summary>
    /// <b>N (Unit; M-7).</b> AC-PIL-f (1): frozen inputs that differ from the fresh derivation in exactly one thing, each
    /// of the eight the refinement names, are refused with kind <c>payroll-inputs-changed</c>, month 2026-03 and the
    /// export's sentence.
    /// <para><b>Red conditions.</b> M-7 (<c>EnsurePayInputsUnchanged</c> compares nothing) returns without throwing, so
    /// <c>Assert.Throws</c> fails with "No exception was thrown" for every row. A comparison that skipped one member
    /// (the configuration runs, the organisation, the cutoff) fails exactly that row.</para>
    /// </summary>
    [Theory]
    [InlineData("config-value")]
    [InlineData("organisation")]
    [InlineData("fraction")]
    [InlineData("position")]
    [InlineData("agreement-code-on-one-day")]
    [InlineData("employed-day-added")]
    [InlineData("employed-day-removed")]
    [InlineData("settlement-cutoff")]
    public void OneDifferenceAtATime_Refused(string difference)
    {
        var ex = Assert.Throws<PayInputsChangedException>(() =>
            PeriodCalculationService.EnsurePayInputsUnchanged(
                FrozenFor(difference), FreshWithOneDifference(difference), Mar01, PeriodCalculationService.PayInputsRoute.Export));

        AssertRefused(ex);
    }

    /// <summary>
    /// <b>N (Unit; M-7).</b> The correction route's refusal carries the correction's sentence, not the export's.
    /// <para><b>Red conditions.</b> M-7: nothing is thrown. A route that always used the export sentence fails the
    /// <c>Assert.Equal</c> on the sentence.</para>
    /// </summary>
    [Fact]
    public void OneDifference_OnTheCorrectionRoute_CarriesTheCorrectionSentence()
    {
        var ex = Assert.Throws<PayInputsChangedException>(() =>
            PeriodCalculationService.EnsurePayInputsUnchanged(
                Frozen(), FreshWithOneDifference("fraction"), Mar01, PeriodCalculationService.PayInputsRoute.Correction));

        AssertRefused(ex, CorrectionSentence);
    }

    // ─── (2) a split frozen run ───────────────────────────────────────────

    /// <summary>
    /// <b>N (Unit; M-7).</b> AC-PIL-f (2): the frozen inputs hold one run for March on version 1; the fresh ones hold
    /// 1-15 March on version 1 and 16-31 March on version 2 (a version dated Monday 16 March 2026), with version 1's
    /// configuration value-equal in both (two distinct instances). The month is refused.
    /// <para><b>Red conditions.</b> M-7: nothing is thrown. A comparison that looked only at the first run (equal in
    /// both) or only at the configuration values present on both sides would pass the split month.</para>
    /// </summary>
    [Fact]
    public void SplitFrozenRun_Refused()
    {
        var frozen = Frozen();
        var fresh = Inputs(
            new[] { Piece(Mar01, Mar15), Piece(Mar16, Mar31) },
            new[]
            {
                Run(Mar01, Mar15, Hk37()), // a distinct instance, value-equal to the frozen run's configuration
                Run(Mar16, Mar31, EngineConfigRequests.With(Hk37(), "WeeklyNormHours", 32m)),
            });

        var ex = Assert.Throws<PayInputsChangedException>(() =>
            PeriodCalculationService.EnsurePayInputsUnchanged(
                frozen, fresh, Mar01, PeriodCalculationService.PayInputsRoute.Export));

        AssertRefused(ex);
    }

    // ─── (3) a day the fresh derivation cannot cover ──────────────────────

    /// <summary>
    /// <b>N (Unit; M-52).</b> AC-PIL-f (3): a fresh derivation that cannot cover a day (a profile resolver that answers
    /// no profile for an EMPLOYED day of March) is refused as <c>payroll-inputs-changed</c>, through the derivation
    /// wrapper, and NEVER as the record-gap refusal (<c>EmployeeProfileNotFoundException</c>), whose message can carry a
    /// day of the employment.
    /// <para><b>Red conditions.</b> M-52 (the wrapper's catch of a refusal returns instead of throwing the new exception)
    /// lets the month pass: <c>Assert.ThrowsAsync&lt;PayInputsChangedException&gt;</c> fails with "No exception was
    /// thrown". Dropping the catch altogether fails it with <c>EmployeeProfileNotFoundException</c> instead. M-7 does not
    /// touch this path: the derivation refuses before the comparison is reached, so this arm stays green under M-7.</para>
    /// </summary>
    [Fact]
    public async Task DayTheFreshDerivationCannotCover_RefusedAsInputsChanged_NeverAsRecordGap()
    {
        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine, profileResolver: new NoProfileForAnyDay());

        var ex = await Assert.ThrowsAsync<PayInputsChangedException>(() =>
            pcs.RecheckPayInputsUnderLockAsync(
                Profile(EmployeeId), Mar01, Mar31, Frozen(), PeriodCalculationService.PayInputsRoute.Export));

        AssertRefused(ex);
        Assert.Empty(engine.Calls); // the recheck calls no rule: it derives inputs only
    }

    // ─── the wrapper's other three refusals (TASK-14847-T, W5) ─────────────

    /// <summary>A configuration lookup whose every read throws what the test gives it.</summary>
    private sealed class ThrowingConfigLookup : IAgreementConfigLookup
    {
        private readonly Func<Exception> _make;

        public ThrowingConfigLookup(Func<Exception> make) => _make = make;

        public Task<AgreementRuleConfig> ResolveAsync(
            string orgId, string agreementCode, string okVersion, string? position, DateOnly asOf, CancellationToken ct = default)
            => throw _make();

        public Task<AgreementRuleConfig> ResolveCentralAsync(
            string agreementCode, string okVersion, DateOnly asOf, CancellationToken ct = default)
            => throw _make();
    }

    /// <summary>
    /// <b>N (Unit; M-53).</b> W5: when the configuration database cannot be read while the fresh derivation runs, the recheck
    /// answers <see cref="RecordingConfigUnavailableException"/> (the recording-time 503), carrying the lookup's exception, and
    /// never <see cref="PayInputsChangedException"/>: an outage is not "the inputs changed".
    /// <para><b>Red conditions.</b> M-53 (the <c>ConfigLookupUnavailableException</c> catch of the recheck is removed): the
    /// lookup's exception propagates as itself, so <c>ThrowsAsync&lt;RecordingConfigUnavailableException&gt;</c> fails on the
    /// exception type. A catch that turned the outage into <c>PayInputsChangedException</c> fails the same way.</para>
    /// </summary>
    [Fact]
    public async Task ConfigurationReadThatFails_RefusedAsRecordingConfigUnavailable_NeverAsInputsChanged()
    {
        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine, configLookup: new ThrowingConfigLookup(
            () => new ConfigLookupUnavailableException("central", new InvalidOperationException("the database is down"))));

        var ex = await Assert.ThrowsAsync<RecordingConfigUnavailableException>(() =>
            pcs.RecheckPayInputsUnderLockAsync(
                Profile(EmployeeId), Mar01, Mar31, Frozen(), PeriodCalculationService.PayInputsRoute.Export));

        Assert.IsType<ConfigLookupUnavailableException>(ex.InnerException);
        Assert.Empty(engine.Calls);
    }

    /// <summary>
    /// <b>N (Unit; M-52).</b> W5: a day with no published central version in force is a day the fresh derivation cannot
    /// cover, so the recheck refuses it as <c>payroll-inputs-changed</c> (the same answer as a record gap).
    /// <para><b>Red conditions.</b> M-52 (the refusal catch returns instead of throwing): the month passes and
    /// <c>ThrowsAsync</c> fails with "No exception was thrown". Dropping <c>NoConfigInForceException</c> from the catch filter
    /// fails it with that exception instead.</para>
    /// </summary>
    [Fact]
    public async Task NoConfigurationInForce_RefusedAsInputsChanged()
    {
        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine, configLookup: new ThrowingConfigLookup(
            () => new NoConfigInForceException("HK", "OK24", Mar01)));

        var ex = await Assert.ThrowsAsync<PayInputsChangedException>(() =>
            pcs.RecheckPayInputsUnderLockAsync(
                Profile(EmployeeId), Mar01, Mar31, Frozen(), PeriodCalculationService.PayInputsRoute.Export));

        AssertRefused(ex);
        Assert.Empty(engine.Calls);
    }

    /// <summary>
    /// <b>N (Unit; M-52).</b> W5: a planner split refusal (a whole-window rule that would be evaluated in two EMPLOYED segments)
    /// is a day-set the fresh derivation cannot plan, so the recheck refuses it as <c>payroll-inputs-changed</c>. The refusal is
    /// made by the real planner: the rule set is the fixture's <c>OVERTIME_CALC</c> as an AlignedWindow rule, and the period
    /// 16 March to 15 April 2026 crosses 1 April, the OK24 to OK26 transition, which cuts it into two EMPLOYED segments.
    /// <para><b>Red conditions.</b> M-52: the month passes and <c>ThrowsAsync</c> fails with "No exception was thrown". Dropping
    /// the <c>IsSplitRefusal</c> arm of the catch filter lets <see cref="PlannerInvariantViolation"/> propagate, failing it on
    /// the type.</para>
    /// </summary>
    [Fact]
    public async Task PlannerSplitRefusal_RefusedAsInputsChanged()
    {
        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine, ruleSet: AlignedWindowRuleSet);

        var ex = await Assert.ThrowsAsync<PayInputsChangedException>(() =>
            pcs.RecheckPayInputsUnderLockAsync(
                Profile(EmployeeId), Mar16, new DateOnly(2026, 4, 15), Frozen(), PeriodCalculationService.PayInputsRoute.Export));

        Assert.Equal(Mar01, ex.Month); // the month of the period's start
        Assert.Equal(ExportSentence, ex.ErrorSentence);
        Assert.Empty(engine.Calls);
    }

    // ─── (4) equal inputs: the control arm ────────────────────────────────

    /// <summary>
    /// <b>N (Unit; control, green under M-7 and M-52).</b> AC-PIL-f (4): two distinct but equal configuration instances,
    /// the same runs and the same edges pass without an exception, on both routes.
    /// <para><b>Red conditions.</b> A comparison by reference (the configuration is a class, so two instances are
    /// different objects) or one that refused on an unrelated member would throw here. The mutations leave this arm
    /// green by design: it proves a mutation that refuses everything is not mistaken for a working check.</para>
    /// </summary>
    [Theory]
    [InlineData(PeriodCalculationService.PayInputsRoute.Export)]
    [InlineData(PeriodCalculationService.PayInputsRoute.Correction)]
    public void EqualInputs_TwoDistinctButEqualConfigInstances_Pass(PeriodCalculationService.PayInputsRoute route)
    {
        var frozen = Frozen();
        var fresh = Inputs(
            new[] { Piece(Mar01, Mar31) },
            new[] { Run(Mar01, Mar31, Hk37()) });
        Assert.NotSame(frozen.ConfigRuns![0].Config, fresh.ConfigRuns![0].Config);

        PeriodCalculationService.EnsurePayInputsUnchanged(frozen, fresh, Mar01, route);
    }

    /// <summary>A profile resolver that finds no profile row for any day: the record gap.</summary>
    private sealed class NoProfileForAnyDay : IEmploymentProfileResolver
    {
        public Task<EmploymentProfile?> GetByEmployeeIdAtAsync(
            string employeeId, DateOnly asOfDate, CancellationToken ct = default)
            => Task.FromResult<EmploymentProfile?>(null);
    }
}
