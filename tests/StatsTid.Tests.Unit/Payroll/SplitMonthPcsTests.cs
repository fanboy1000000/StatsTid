using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S145 Stage B / TASK-14524 (pins for TASK-14520 and TASK-14522) - what the payroll calculation service
/// does with a month that has two or more EMPLOYED parts, DB-free, through the plan-taking entry point
/// <c>CalculateWithOutcomeAsync(plan, ...)</c> with a CALLER-BUILT plan.
///
/// <para>
/// Plain language: after Stage B a month with a mid-month change is paid part by part. The one rule-engine
/// answer for overtime, norm and flex covers the WHOLE month, so the report must show that one answer, not
/// the pieces it was cut into to map each line under its part's wage-type codes (TASK-14522 contract 2);
/// the engine must be asked about the whole month in one call whose pieces cover both parts; the norm
/// totals of a starter or leaver month must survive the merge with no "No RuleClassification
/// registered" warning; and a line the engine dates inside no employed part must fail LOUDLY instead of
/// being silently filed under the neighbouring part (a wrong wage-type code reported as success).
/// </para>
///
/// <para>
/// <b>C-U1 (binding).</b> No fact here plans a month with two or more EMPLOYED segments through the PCS
/// builder (the planless shim or <c>BuildPlanWithHistoriesAsync</c>) with the live registry set: every
/// plan is built by the caller. That keeps MB-7 (the evaluated-set filter) a Docker mutation.
/// </para>
///
/// <para>
/// <b>Fixture limit (reported).</b> This harness has no wage-type mapping store (<c>mappingService</c> is
/// <c>null!</c>), so the timeline answers here carry NO line items, except in the Q-A Theory where the
/// throw happens before any mapping. "Every line of both segments in the merged row" and "export lines
/// mapped per segment" are therefore pinned at Docker level (SP1, WS1, QA1), and the merged-row facts here
/// pin Success and the norm totals.
/// </para>
///
/// NEW-API pins (they name <c>SplitBehavior.TimelineAware</c> and <c>MergeStrategy.SinglePlanEvaluation</c>
/// or rest on TASK-14522 contract 7): RED by Unit mutation at the B.3 gate (MB-8, MB-3, MB-10c, MB-12).
/// </summary>
public sealed class SplitMonthPcsTests
{
    private const string EmployeeId = "EMP-S145-SPLIT";

    /// <summary>The Stage B classification of the three timeline rules, mirrored for a hand-built set.</summary>
    private static readonly RuleClassification[] StageBTripleSet =
    {
        new("SUPPLEMENT_CALC", Span.Entry,       SplitBehavior.SegmentSafe,    Family.Calculation, MergeStrategy.Concatenate,           SnapshotContract: null),
        new("ON_CALL_DUTY",    Span.Entry,       SplitBehavior.SegmentSafe,    Family.Calculation, MergeStrategy.Concatenate,           SnapshotContract: null),
        new("OVERTIME_CALC",   Span.Window,      SplitBehavior.TimelineAware,  Family.Calculation, MergeStrategy.SinglePlanEvaluation,  SnapshotContract: null),
        new("NORM_CHECK_37H",  Span.Period,      SplitBehavior.TimelineAware,  Family.Calculation, MergeStrategy.SinglePlanEvaluation,  SnapshotContract: null),
        new("FLEX_BALANCE",    Span.CrossPeriod, SplitBehavior.TimelineAware,  Family.Calculation, MergeStrategy.SinglePlanEvaluation,  SnapshotContract: null),
    };

    private sealed record TimelineLine(string Date, decimal Hours, string TimeType = "OVERTIME_50");

    /// <summary>
    /// A complete timeline answer (three rows, the flex block, fingerprints, marker) with optional lines on
    /// the overtime row and optional norm totals on the norm row.
    /// </summary>
    private static HttpResponseMessage Timeline(
        IReadOnlyList<TimelineLine>? overtimeLines = null,
        decimal? normHours = null, decimal? actualHours = null, decimal? deviation = null)
    {
        object Row(string ruleId, object[]? lines = null, bool withNorm = false) => withNorm
            ? new
            {
                ruleId, employeeId = "EMP", success = true,
                lineItems = lines ?? Array.Empty<object>(),
                normPeriodWeeks = 5, normHoursTotal = normHours, actualHoursTotal = actualHours,
                deviation, normFulfilled = true,
            }
            : (object)new { ruleId, employeeId = "EMP", success = true, lineItems = lines ?? Array.Empty<object>() };

        var otLines = (overtimeLines ?? Array.Empty<TimelineLine>())
            .Select(l => (object)new { timeType = l.TimeType, hours = l.Hours, rate = 1.5m, date = l.Date })
            .ToArray();

        var payload = new
        {
            results = new[] { Row("OVERTIME_CALC", otLines), Row("NORM_CHECK_37H", null, withNorm: true), Row("FLEX_BALANCE") },
            flex = new
            {
                ruleId = "FLEX_BALANCE", employeeId = "EMP", success = true,
                lineItems = Array.Empty<object>(),
                previousBalance = 0m, newBalance = 0m, delta = 0m, workedHours = 0m,
                absenceNormCredits = 0m, effectiveNorm = 0m, excessForPayout = 0m,
            },
            configFingerprints = new[] { "stub" },
            evaluationModel = "weekly-timeline-v1",
        };
        return Json(payload);
    }

    private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            Encoding.UTF8, "application/json"),
    };

    /// <summary>The engine: the given timeline answer; the per-segment calls answer with no lines, the absence rule under its real id.</summary>
    private static RecordingRuleEngine Engine(Func<HttpResponseMessage> timeline) => new(call =>
    {
        if (call.Path.EndsWith(RecordingRuleEngine.TimelinePath, StringComparison.Ordinal))
            return timeline();
        if (call.Path.EndsWith("/evaluate-absence", StringComparison.Ordinal))
            return Json(new { ruleId = "ABSENCE_CALC", employeeId = "EMP", success = true, lineItems = Array.Empty<object>() });
        return RecordingRuleEngine.AllSucceedWithNoLineItems(call);
    });

    // -- MB-8 pin: the merged rows are the timeline rows ----------------------------------------

    /// <summary>
    /// A profile change on Mon 16 Mar 2026 makes two EMPLOYED segments. The rule set mirrors the Stage B
    /// triple; the engine answers the timeline with the norm totals. Each of the three timeline rules must
    /// reach the report as ONE row, successful, with the norm totals intact (the merger throws on two rows
    /// for a <c>SinglePlanEvaluation</c> rule, so a merge fed the per-segment pieces would show a FAILED row
    /// and the pieces' lost totals).
    ///
    /// Red conditions: MB-8 (Unit mutation; the merge is given the per-segment rows again) makes each
    /// timeline rule's row <c>Success == false</c> (the merger's two-results throw), tripping the first
    /// <c>Assert.True(row.Success)</c>.
    /// </summary>
    [Fact]
    public async Task TwoEmployedSegments_TimelineAwareRules_MergedRowIsTheTimelineRow()
    {
        var profileChange = new DateOnly(2026, 3, 16);
        var plan = Plan(EmployeeId, Mar01, Mar31, windows: null, ruleSet: StageBTripleSet,
            profileChangeDates: new[] { profileChange });
        Assert.Equal(2, plan.Segments.Count);
        Assert.All(plan.Segments, s => Assert.Equal(EmploymentWindowStatus.EMPLOYED, s.EmploymentStatus));

        var engine = Engine(() => Timeline(normHours: 147.40m, actualHours: 150.00m, deviation: 2.60m));
        var pcs = BuildPcs(engine, ruleSet: StageBTripleSet, hoursReader: WeekdayHours(EmployeeId, new DateOnly(2026, 2, 23), Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(plan, Profile(EmployeeId), previousFlexBalance: 0m);

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        foreach (var ruleId in new[] { "OVERTIME_CALC", "NORM_CHECK_37H", "FLEX_BALANCE" })
        {
            var row = Assert.Single(outcome.Result.RuleResults, r => r.RuleId == ruleId);
            Assert.True(row.Success, $"{ruleId}: {row.ErrorMessage}");
            Assert.Null(row.ErrorMessage);
        }

        var norm = Assert.Single(outcome.Result.RuleResults, r => r.RuleId == "NORM_CHECK_37H");
        Assert.Equal(147.40m, norm.NormHoursTotal);
        Assert.Equal(150.00m, norm.ActualHoursTotal);
        Assert.Equal(2.60m, norm.Deviation);
        Assert.Equal(5, norm.NormPeriodWeeks);
        Assert.Equal(RuleResultsPerPlan, outcome.Result.RuleResults.Count);

        // One timeline call for the whole plan: the rows were never evaluated per segment.
        Assert.Single(engine.TimelineCalls);
        Assert.DoesNotContain(engine.PerSegmentCalls, c => c.RuleId is "OVERTIME_CALC" or "NORM_CHECK_37H" or "FLEX_BALANCE");
    }

    // -- MB-3 pin: the timeline pieces cover both parts -----------------------------------------

    /// <summary>
    /// One timeline call; one piece ends Sun 15 Mar, the next starts Mon 16 Mar, the last ends Tue 31 Mar,
    /// and the pieces run gap-free from the opening Monday (23 Feb). Beside the existing
    /// <c>EmploymentWindowAlignedWindowMergeTests</c> assertions (:98-100), now for the Stage B rule set.
    ///
    /// Red conditions: MB-3 (Unit mutation; the per-plan lane covers only the first EMPLOYED segment,
    /// <c>PeriodCalculationService.cs:752-792</c>) drops the 16 Mar piece, tripping the <c>From == 16 Mar</c>
    /// assertion (and the last piece would end 15 Mar, not 31 Mar).
    /// </summary>
    [Fact]
    public async Task TwoEmployedSegments_TimelinePiecesCoverBothSegments()
    {
        var profileChange = new DateOnly(2026, 3, 16);
        var plan = Plan(EmployeeId, Mar01, Mar31, windows: null, ruleSet: StageBTripleSet,
            profileChangeDates: new[] { profileChange });
        var engine = Engine(() => Timeline());
        var pcs = BuildPcs(engine, ruleSet: StageBTripleSet, hoursReader: WeekdayHours(EmployeeId, new DateOnly(2026, 2, 23), Mar31));

        await pcs.CalculateWithOutcomeAsync(plan, Profile(EmployeeId), previousFlexBalance: 0m);

        var timeline = Assert.Single(engine.TimelineCalls);
        Assert.Contains(timeline.Pieces!, p => p.To == new DateOnly(2026, 3, 15));
        Assert.Contains(timeline.Pieces!, p => p.From == profileChange);
        Assert.Equal(Mar31, timeline.Pieces![^1].To);
        AssertPiecesRun(timeline.Pieces, new DateOnly(2026, 2, 23), Mar31);
    }

    // -- MB-10c pin: the live registry set, a leaver month --------------------------------------

    /// <summary>
    /// The FULL live registry set, a caller-built leaver plan (EMPLOYED to Sun 15 Mar, then NOT_EMPLOYED) and
    /// a capturing logger. A leaver month has two plan segments, so it always reaches the merge: before
    /// Stage B <c>NORM_CHECK_37H</c> was unregistered, so its merged row fell back to Concatenate WITH a
    /// warning and lost <c>NormHoursTotal</c>. After Stage B no such warning is logged and the norm row keeps
    /// the period figures (TASK-14520 Fact 5).
    ///
    /// Red conditions: MB-10c (Unit mutation; the <c>NORM_CHECK_37H</c> registration removed) logs the
    /// "No RuleClassification registered" warning for it and nulls its totals, tripping both the
    /// no-warning assertion and <c>Assert.Equal(80.00m, norm.NormHoursTotal)</c>.
    /// </summary>
    [Fact]
    public async Task LiveRegistrySet_LeaverMonth_NoRuleClassificationWarning_NormRowKeepsPeriodFigures()
    {
        var live = new RuleRegistry().GetAll();
        var lastDay = new DateOnly(2026, 3, 15);
        var plan = Plan(EmployeeId, Mar01, Mar31,
            windows: new[] { new EmploymentWindow(null, lastDay) }, ruleSet: live);
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[0].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[1].EmploymentStatus);

        var engine = Engine(() => Timeline(normHours: 80.00m, actualHours: 81.00m, deviation: 1.00m));
        var logger = new CapturingLogger();
        var pcs = new PeriodCalculationService(
            new SingleClientHttpFactory(engine),
            mappingService: null!,
            new InMemoryEventStore(),
            connectionFactory: null!,
            Configuration(),
            logger,
            classificationProvider: new FixedProvider(live),
            hoursReader: WeekdayHours(EmployeeId, new DateOnly(2026, 2, 23), Mar31),
            priorPeriodApprovalReader: new AlwaysApprovedPriorPeriods());

        var outcome = await pcs.CalculateWithOutcomeAsync(plan, Profile(EmployeeId), previousFlexBalance: 0m);

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("No RuleClassification registered", StringComparison.Ordinal));

        var norm = Assert.Single(outcome.Result.RuleResults, r => r.RuleId == "NORM_CHECK_37H");
        Assert.True(norm.Success, norm.ErrorMessage);
        Assert.Equal(80.00m, norm.NormHoursTotal);
        Assert.Equal(81.00m, norm.ActualHoursTotal);
        Assert.Equal(1.00m, norm.Deviation);
    }

    // -- MB-12 pin: a line dated in no EMPLOYED segment refuses ---------------------------------

    /// <summary>
    /// (i) a one-spell plan with an <c>OVERTIME_CALC</c> line dated the day before the period start (a
    /// context day, Sat 28 Feb); (ii) a caller-built plan EMPLOYED 1-10 March, NOT_EMPLOYED 11-20 March,
    /// EMPLOYED 21-31 March, with a line dated Sun 15 March (a gap day). The recording engine returns the
    /// line. The calculation must throw <see cref="InvalidOperationException"/> (exactly that type, the
    /// evaluation-invariant shape, not <see cref="PlannerInvariantViolation"/>) naming the rule id and
    /// carrying no ISO date, and NOTHING is persisted: no manifest event, no legacy completed event.
    ///
    /// <para>
    /// Plain language: today such a line is silently filed under the neighbouring part, a wrong wage-type
    /// code reported as success; the contract makes it a loud 500 with nothing written (TASK-14522
    /// contract 7, ruled R-56).
    /// </para>
    ///
    /// Red conditions: MB-12 (Unit mutation; the throw replaced by today's fallback) lets the line be filed
    /// under a neighbouring segment and on to mapping, where this fixture's null mapping service throws a
    /// <c>NullReferenceException</c>; the call then does not throw the invariant exception and
    /// <c>Assert.ThrowsAsync&lt;InvalidOperationException&gt;</c> trips on the exact type ("Exception type was not
    /// an exact match"); it is not a call that passes without throwing.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PerPlanLineDatedOutsideEveryEmployedSegment_Throws_NothingPersisted(int scenario)
    {
        PlannedCalculation plan;
        string lineDate;
        if (scenario == 1)
        {
            plan = Plan(EmployeeId, Mar01, Mar31, windows: null);
            lineDate = "2026-02-28";
        }
        else
        {
            plan = Plan(EmployeeId, Mar01, Mar31, windows: new[]
            {
                new EmploymentWindow(null, new DateOnly(2026, 3, 10)),
                new EmploymentWindow(new DateOnly(2026, 3, 21), null),
            });
            lineDate = "2026-03-15";
            Assert.Equal(3, plan.Segments.Count);
            Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[1].EmploymentStatus);
        }

        var engine = Engine(() => Timeline(new[] { new TimelineLine(lineDate, 1.00m) }));
        var store = new InMemoryEventStore();
        var pcs = BuildPcs(engine, eventStore: store, hoursReader: WeekdayHours(EmployeeId, new DateOnly(2026, 2, 23), Mar31));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pcs.CalculateWithOutcomeAsync(plan, Profile(EmployeeId), previousFlexBalance: 0m));

        Assert.Contains("OVERTIME_CALC", ex.Message);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", ex.Message);
        Assert.DoesNotContain(lineDate, ex.Message);
        Assert.DoesNotContain(EmployeeId, ex.Message);

        // Nothing was persisted: the manifest and the legacy completed event follow the merge.
        Assert.Empty(store.All);
    }

    // -- helpers -------------------------------------------------------------------------------

    private sealed class FixedProvider : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public FixedProvider(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }

    private sealed class CapturingLogger : ILogger<PeriodCalculationService>
    {
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages
        {
            get { lock (_messages) return _messages.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_messages) _messages.Add(formatter(state, exception));
        }
    }
}
