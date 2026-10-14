using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S137 / TASK-13702 — <see cref="PeriodCalculationService"/> obeys the typed segments of
/// ADR-040 D5 at evaluation time. Locally runnable (no DB, no Docker).
///
/// <para>
/// Plain-language: once the planner marks a segment NOT_EMPLOYED (the days before a hire or
/// after a leave), payroll must do NOTHING for it — no "which profile applied?" lookup, no
/// rule-engine calls, no export lines — while still recording it in the audit manifest. And
/// two bookkeeping bugs the skip would otherwise expose are fixed and pinned here:
/// <list type="bullet">
///   <item><b>Codex-B1 — ordering (ADR-040 D10):</b> the skip runs BEFORE profile resolution.
///     A post-S136 employee's profile row starts at hire, so asking "what profile applied on
///     a pre-hire date?" would trip the resolver's fail-closed path — a real 500. Pinned via a
///     counting profile resolver: no pre-hire date is ever asked about.</item>
///   <item><b>Codex-B2 — the total-failure short-circuit, both halves:</b> the per-segment
///     attempt budget used to be captured from SEGMENT 0 (zero if segment 0 is skipped) and
///     multiplied by the TOTAL segment count (over-stated by 6 per skipped segment). Either
///     half alone lets a calculation whose every real rule call failed return "Success" with
///     zero rule results. Pinned in both directions — a fully-failed windowed plan DOES
///     short-circuit; a healthy one does NOT.</item>
///   <item><b>"Nothing to calculate" is a success:</b> a plan with zero employed segments
///     makes no rule-engine call (so nothing can fail, S145 R-6), and returns Success with no results, no
///     lines, and a manifest of all-NOT_EMPLOYED segments — the ruled semantics.</item>
///   <item><b>The opening flex balance reaches the one timeline call, and a skipped middle span is never covered</b> (S145 F1; an empty
///     result list yields no flex delta, and no row is ever synthesized for it).</item>
/// </list>
/// </para>
/// </summary>
public sealed class EmploymentWindowSegmentSkipTests
{
    private const string EmployeeId = "EMP-S137-SKIP";

    /// <summary>S145 R-6 (adjudicator): the one fixed sentence of a refused (partially or wholly failed) calculation.</summary>
    private const string RuleEngineFailureMessage = "Rule engine evaluation failed; no payroll figure is produced for this period";

    // ═════════════════════════════════════════════════════════════════════
    // Codex-B2 — the short-circuit, both halves
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>First half: segment 0 is NOT_EMPLOYED (a mid-month starter). The attempt
    /// budget must be captured from the first EMPLOYED segment — capturing from segment 0
    /// would leave it at 0 and disarm the short-circuit entirely.</summary>
    [Fact]
    public async Task Segment0NotEmployed_EveryEmployedEvaluationFails_ShortCircuits()
    {
        var hire = new DateOnly(2026, 3, 10);
        var plan = Plan(EmployeeId, Mar01, Mar31, new[] { new EmploymentWindow(hire, null) });
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[0].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[1].EmploymentStatus);

        var engine = new RecordingRuleEngine(RecordingRuleEngine.AllFail);
        var pcs = BuildPcs(engine, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId),
            previousFlexBalance: 0m);

        Assert.False(outcome.Result.Success);
        // S145 R-6 (adjudicator): any failed rule-engine call refuses the whole calculation, one fixed sentence.
        Assert.Equal(RuleEngineFailureMessage, outcome.Result.ErrorMessage);
        Assert.Equal(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);

        // S145 R14: exactly one employed segment's worth of attempts, none for the pre-hire span: the
        // plan's one timeline call + 3 per-segment calls (was 6 per-segment calls).
        Assert.Equal(TimelineCallsPerPlan + PerSegmentCallsPerEmployedSegment, engine.Calls.Count);
        // S145 F1: the per-segment calls start at the hire date; the timeline call is the whole month's
        // (its range is the period), so what must start at the hire is its PIECES and its entries.
        Assert.All(engine.PerSegmentCalls, c => Assert.Equal(hire, c.PeriodStart));
        var timeline = Assert.Single(engine.TimelineCalls);
        Assert.Equal(hire, timeline.Pieces![0].From);
        Assert.All(timeline.TimelineEntries!, e => Assert.True(e.Date >= hire, $"{e.Date} is before the hire date"));
    }

    /// <summary>Second half — the denominator. A leaver plan (EMPLOYED / NOT_EMPLOYED) whose
    /// six real calls all fail: with the old <c>budget × plan.Segments.Count</c> the check
    /// compared 6 failures against 12 and did NOT short-circuit (a "successful" calculation
    /// with zero rule results). It must short-circuit.</summary>
    [Fact]
    public async Task LeaverPlan_EveryEmployedEvaluationFails_ShortCircuits()
    {
        var lastDay = new DateOnly(2026, 3, 15);
        var plan = Plan(EmployeeId, Mar01, Mar31, new[] { new EmploymentWindow(null, lastDay) });
        Assert.Equal(2, plan.Segments.Count);

        var engine = new RecordingRuleEngine(RecordingRuleEngine.AllFail);
        var pcs = BuildPcs(engine, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId),
            previousFlexBalance: 0m);

        Assert.False(outcome.Result.Success);
        // S145 R-6 (adjudicator): any failed rule-engine call refuses the whole calculation, one fixed sentence.
        Assert.Equal(RuleEngineFailureMessage, outcome.Result.ErrorMessage);
        Assert.Empty(outcome.Result.ExportLines);
        Assert.Equal(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);
        // S145 R14: the real calls are the plan's one timeline call + 3 per-segment calls = 4 (was 6).
        Assert.Equal(TimelineCallsPerPlan + PerSegmentCallsPerEmployedSegment, engine.Calls.Count);
    }

    // ═════════════════════════════════════════════════════════════════════
    // S145 R-6 — any failed rule-engine call refuses the whole calculation
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Adjudicator R-6 (at TASK-14511's return): a partial failure is a refusal.</b> The timeline
    /// call (overtime, norm, flex, judged week by week) returns 500 while every per-segment call
    /// succeeds. Before the ruling the month reported Success with overtime, norm and flex silently
    /// missing; now no payroll figure is produced: Success false, no export lines, no manifest event,
    /// the fixed sentence.
    ///
    /// <para>Red conditions: mutation MU-44 ("partial failure tolerated: restore
    /// <c>callsFailed &gt;= callsAttempted</c>" in <c>PeriodCalculationService</c>) lets the month
    /// through as Success and trips the first assertion. Observed RED on c775e92, before the fix-up.</para>
    /// </summary>
    [Fact]
    public async Task TimelineCallFails_PerSegmentCallsSucceed_CalculationRefused_NothingWritten()
    {
        var plan = Plan(EmployeeId, Mar01, Mar31, windows: null);
        var engine = new RecordingRuleEngine(call =>
            call.Path.EndsWith(RecordingRuleEngine.TimelinePath, StringComparison.Ordinal)
                ? RecordingRuleEngine.AllFail(call)
                : RecordingRuleEngine.AllSucceedWithNoLineItems(call));
        var eventStore = new InMemoryEventStore();
        var pcs = BuildPcs(engine, eventStore, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(plan, Profile(EmployeeId), previousFlexBalance: 0m);

        // The timeline WAS attempted and the per-segment calls DID run: this is a partial failure.
        Assert.Single(engine.TimelineCalls);
        Assert.NotEmpty(engine.PerSegmentCalls);

        Assert.False(outcome.Result.Success);
        Assert.Equal(RuleEngineFailureMessage, outcome.Result.ErrorMessage);
        Assert.Empty(outcome.Result.ExportLines);
        Assert.Empty(outcome.Result.RuleResults);
        Assert.Equal(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);
        Assert.Null(eventStore.Manifest);
    }

    /// <summary>
    /// <b>Adjudicator R-6, the other direction:</b> the timeline succeeds and ONE per-segment call
    /// (absence) returns 500. Still a refusal: Success false, no export lines, no manifest event,
    /// the fixed sentence. Red conditions: mutation MU-44 (see above) lets this month through.
    /// </summary>
    [Fact]
    public async Task OnePerSegmentCallFails_TimelineSucceeds_CalculationRefused_NothingWritten()
    {
        var plan = Plan(EmployeeId, Mar01, Mar31, windows: null);
        var engine = new RecordingRuleEngine(call =>
            call.Path.EndsWith("/evaluate-absence", StringComparison.Ordinal)
                ? RecordingRuleEngine.AllFail(call)
                : RecordingRuleEngine.AllSucceedWithNoLineItems(call));
        var eventStore = new InMemoryEventStore();
        var pcs = BuildPcs(engine, eventStore, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(plan, Profile(EmployeeId), previousFlexBalance: 0m);

        Assert.Single(engine.TimelineCalls);
        Assert.Contains(engine.Calls, c => c.Path.EndsWith("/evaluate-absence", StringComparison.Ordinal));

        Assert.False(outcome.Result.Success);
        Assert.Equal(RuleEngineFailureMessage, outcome.Result.ErrorMessage);
        Assert.Empty(outcome.Result.ExportLines);
        Assert.Empty(outcome.Result.RuleResults);
        Assert.Equal(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);
        Assert.Null(eventStore.Manifest);
    }

    // ═════════════════════════════════════════════════════════════════════
    // S145 R-6 (Codex WARNING 2) — a timeline reply is validated for SUCCESS, not just presence
    // ═════════════════════════════════════════════════════════════════════

    private async Task AssertTimelineReplyRefusedAsync(HttpResponseMessage reply)
    {
        var plan = Plan(EmployeeId, Mar01, Mar31, windows: null);
        var engine = new RecordingRuleEngine(call =>
            call.Path.EndsWith(RecordingRuleEngine.TimelinePath, StringComparison.Ordinal)
                ? reply
                : RecordingRuleEngine.AllSucceedWithNoLineItems(call));
        var eventStore = new InMemoryEventStore();
        var pcs = BuildPcs(engine, eventStore, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(plan, Profile(EmployeeId), previousFlexBalance: 0m);

        // The timeline answered HTTP 200 and was structurally complete.
        Assert.Single(engine.TimelineCalls);
        Assert.False(outcome.Result.Success);
        Assert.Equal(RuleEngineFailureMessage, outcome.Result.ErrorMessage);
        Assert.Empty(outcome.Result.ExportLines);
        Assert.Empty(outcome.Result.RuleResults);
        Assert.Equal(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);
        Assert.Null(eventStore.Manifest);
    }

    /// <summary>
    /// <b>R-6 (Codex WARNING 2).</b> A structurally complete HTTP 200 whose overtime row says
    /// <c>success: false</c> is a failed evaluation, not a payroll figure. Red conditions: the timeline
    /// validator checks presence only (the state on b5f29eb), so the month calculates as Success.
    /// </summary>
    [Fact]
    public Task TimelineReply_FailedResultRow_CalculationRefused_NothingWritten()
        => AssertTimelineReplyRefusedAsync(RecordingRuleEngine.TimelineSuccess(failedOvertimeRow: true));

    /// <summary>
    /// <b>R-6 (Codex WARNING 2).</b> The flex block carries a <c>success</c> field (<c>FlexEvaluationResponse</c>);
    /// a 200 with <c>flex.success: false</c> is refused. Red conditions as above.
    /// </summary>
    [Fact]
    public Task TimelineReply_FailedFlexBlock_CalculationRefused_NothingWritten()
        => AssertTimelineReplyRefusedAsync(RecordingRuleEngine.TimelineSuccess(failedFlexBlock: true));

    /// <summary>
    /// <b>R-6 (Codex WARNING 2).</b> An empty <c>configFingerprints</c> list means the answer cannot be
    /// replayed (TASK-14513 records it); a 200 with no fingerprint is refused. Red conditions as above.
    /// </summary>
    [Fact]
    public Task TimelineReply_EmptyFingerprints_CalculationRefused_NothingWritten()
        => AssertTimelineReplyRefusedAsync(RecordingRuleEngine.TimelineSuccess(emptyFingerprints: true));

    // ═════════════════════════════════════════════════════════════════════
    // S145 R-10 — the caller-position fallback is retired for every piece
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>R-10 (adjudicator, on Codex WARNING 1).</b> A dated <c>employee_profiles</c> row with a null
    /// position means "no position (base config)"; it is never filled in from the request's profile.
    /// September 2026: the employee becomes DEPARTMENT_HEAD on 1 Sep (the resolver answers null before
    /// that date). The context piece covering Monday 31 Aug must carry NO position; the pieces covering
    /// 1-30 Sep carry DEPARTMENT_HEAD. The caller's profile also says DEPARTMENT_HEAD, which must NOT leak
    /// back onto 31 Aug.
    ///
    /// <para>Red conditions: mutation MU-45 ("restore the caller-position fallback" in
    /// <c>PeriodCalculationService</c>'s profile resolution) puts DEPARTMENT_HEAD on the 31 Aug piece and
    /// trips the first position assertion. Observed RED on b5f29eb, before the fix-up.</para>
    /// </summary>
    [Fact]
    public async Task ContextPiece_PromotedToDepartmentHead1Sep2026_31AugPieceCarriesNoPosition_CallerPositionNotInherited()
    {
        var sep01 = new DateOnly(2026, 9, 1);
        var sep30 = new DateOnly(2026, 9, 30);
        var aug31 = new DateOnly(2026, 8, 31);
        Assert.Equal(DayOfWeek.Monday, aug31.DayOfWeek);
        Assert.Equal(DayOfWeek.Tuesday, sep01.DayOfWeek);

        var caller = Profile(EmployeeId) with { AgreementCode = "AC", Position = "DEPARTMENT_HEAD", OkVersion = "OK26" };
        var plan = Plan(EmployeeId, sep01, sep30, windows: null);
        var engine = new RecordingRuleEngine();
        var resolver = new CountingProfileResolver(d => d >= sep01 ? "DEPARTMENT_HEAD" : null, agreementCode: "AC");
        var pcs = BuildPcs(engine, profileResolver: resolver, hoursReader: WeekdayHours(EmployeeId, aug31, sep30));

        var outcome = await pcs.CalculateWithOutcomeAsync(plan, caller, previousFlexBalance: 0m);
        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);

        var pieces = Assert.Single(engine.TimelineCalls).Pieces!;
        AssertPiecesRun(pieces, aug31, sep30);
        var onAug31 = Assert.Single(pieces, p => p.From <= aug31 && aug31 <= p.To);
        Assert.Null(onAug31.Position);
        var inSeptember = pieces.Where(p => p.To >= sep01).ToList();
        Assert.NotEmpty(inSeptember);
        Assert.All(inSeptember, p => Assert.Equal("DEPARTMENT_HEAD", p.Position));
    }

    /// <summary>
    /// <b>R-10 (adjudicator, on Codex WARNING 1).</b> The dated row has no position on ANY date, while
    /// the caller's profile says DEPARTMENT_HEAD: every piece sent carries <c>Position == null</c>. The
    /// caller's position is never substituted. Red conditions: mutation MU-45 ("restore the caller-position
    /// fallback") gives every piece DEPARTMENT_HEAD. Observed RED on b5f29eb, before the fix-up.
    /// </summary>
    [Fact]
    public async Task InMonthPieces_DatedRowHasNoPosition_CallerPositionNotInherited()
    {
        var caller = Profile(EmployeeId) with { Position = "DEPARTMENT_HEAD" };
        var plan = Plan(EmployeeId, Mar01, Mar31, windows: null);
        var engine = new RecordingRuleEngine();
        var resolver = new CountingProfileResolver(_ => null);
        var pcs = BuildPcs(engine, profileResolver: resolver, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(plan, caller, previousFlexBalance: 0m);
        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);

        var pieces = Assert.Single(engine.TimelineCalls).Pieces!;
        Assert.NotEmpty(pieces);
        Assert.All(pieces, p => Assert.Null(p.Position));
    }

    // ═════════════════════════════════════════════════════════════════════
    // S145 R-12 — the result reports the agreement the period was evaluated under
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>R-12 (adjudicator, at TASK-14513's return).</b> The result's <c>AgreementCode</c> is the agreement
    /// the period was EVALUATED under (the first EMPLOYED segment's frozen piece), never the agreement the
    /// request body carried. The caller's body says "HK" while the dated row for March says "AC": the engine
    /// is sent AC pieces (the dated row decides, as with R-10's position), so a result and a
    /// <c>PeriodCalculationCompleted</c> event saying "HK" would describe a month that never ran under HK.
    ///
    /// <para>Red conditions: today <c>EvaluateAsync</c> stamps <c>profile.AgreementCode</c> (the caller's) on
    /// the success result and on the legacy completed event, so both assertions read "HK". Observed RED on
    /// 9984173, before the fix-up. The same one-derivation mutation (read the caller's code instead of
    /// <c>FirstEmployedSegmentPiece(...)?.AgreementCode</c>) trips them after it.</para>
    /// </summary>
    [Fact]
    public async Task ForwardCalc_RequestAgreementDiffersFromDatedRecord_ResultReportsDatedAgreement()
    {
        // S145 R-12:
        var caller = Profile(EmployeeId) with { AgreementCode = "HK" };
        var plan = Plan(EmployeeId, Mar01, Mar31, windows: null);
        var engine = new RecordingRuleEngine();
        var resolver = new CountingProfileResolver(_ => null, agreementCode: "AC");
        var eventStore = new InMemoryEventStore();
        var pcs = BuildPcs(engine, eventStore, profileResolver: resolver, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(plan, caller, previousFlexBalance: 0m);
        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);

        // The engine really ran under the dated agreement (so "AC" is what the period was evaluated under).
        var pieces = Assert.Single(engine.TimelineCalls).Pieces!;
        Assert.NotEmpty(pieces);
        // S145 R-24: every piece the engine was sent carries the dated agreement, not the body's "HK".
        Assert.All(pieces, p => Assert.Equal("AC", p.AgreementCode));

        Assert.Equal("AC", outcome.Result.AgreementCode);
        // The legacy completed event reports the same single derivation.
        var completed = Assert.Single(eventStore.All.OfType<PeriodCalculationCompleted>());
        Assert.Equal("AC", completed.AgreementCode);
    }

    /// <summary>The healthy twin: the same leaver plan with a working rule engine must NOT
    /// short-circuit — the fix narrows the denominator, it must not over-trigger.</summary>
    [Fact]
    public async Task LeaverPlan_EmployedEvaluationsSucceed_DoesNotShortCircuit()
    {
        var lastDay = new DateOnly(2026, 3, 15);
        var plan = Plan(EmployeeId, Mar01, Mar31, new[] { new EmploymentWindow(null, lastDay) });

        var engine = new RecordingRuleEngine();
        var eventStore = new InMemoryEventStore();
        var pcs = BuildPcs(engine, eventStore, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId),
            previousFlexBalance: 0m);

        Assert.True(outcome.Result.Success);
        Assert.Null(outcome.Result.ErrorMessage);
        // One employed segment → one result per stubbed rule (overtime, norm and flex from the timeline; supplement, on-call and absence per segment).
        Assert.Equal(RuleResultsPerPlan, outcome.Result.RuleResults.Count);
        Assert.All(outcome.Result.RuleResults, r => Assert.True(r.Success));
        Assert.NotEqual(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);

        // The rule engine was only ever asked about the employed span [03-01 .. 03-15].
        // S145 R14: 1 timeline call + 3 per-segment calls = 4 (was 6 per-segment calls).
        Assert.Equal(TimelineCallsPerPlan + PerSegmentCallsPerEmployedSegment, engine.Calls.Count);
        Assert.All(engine.PerSegmentCalls, c =>
        {
            Assert.Equal(Mar01, c.PeriodStart);
            Assert.Equal(lastDay, c.PeriodEnd); // the last employed day IS inside the evaluated range
        });
        // S145 F1: the timeline call carries the period's range; its PIECES end on the last employed
        // day, start on the month's opening Monday (23 Feb: the week holding Sunday 1 Mar is March's)
        // and leave no day uncovered between; no entry is sent after the last employed day.
        var timeline = Assert.Single(engine.TimelineCalls);
        Assert.Equal(Mar01, timeline.PeriodStart);
        Assert.Equal(Mar31, timeline.PeriodEnd);
        AssertPiecesRun(timeline.Pieces, new DateOnly(2026, 2, 23), lastDay);
        Assert.All(timeline.TimelineEntries!, e => Assert.True(e.Date <= lastDay, $"{e.Date} is after the last employed day"));
        // 10 weekdays (2-6 and 9-13 March) fall in the employed span of the hours read.
        Assert.Equal(10, timeline.TimelineEntries!.Count);

        // ...and the manifest still records the NOT_EMPLOYED suffix (auditability).
        var manifest = eventStore.Manifest;
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Segments.Count);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, manifest.Segments[1].EmploymentStatus);
        Assert.Equal(lastDay.AddDays(1), manifest.Segments[1].StartDate);
        Assert.Contains("EmploymentEnded", manifest.BoundaryCauseSummary);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Zero employed segments — "nothing to calculate" is a success
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>An EMPTY windows list ("the window is known; no employed day falls in the
    /// period") types the whole plan NOT_EMPLOYED: budget 0 → no short-circuit → Success,
    /// zero results, zero lines, zero rule-engine calls, zero profile lookups — and a
    /// manifest whose only segment is NOT_EMPLOYED, so the audit trail records WHY nothing
    /// was paid.</summary>
    [Fact]
    public async Task AllNotEmployedPlan_SucceedsWithNothingToCalculate_ManifestStillEmitted()
    {
        var plan = Plan(EmployeeId, Mar01, Mar31, Array.Empty<EmploymentWindow>());
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, Assert.Single(plan.Segments).EmploymentStatus);

        var engine = new RecordingRuleEngine(RecordingRuleEngine.AllFail); // would fail IF called
        var eventStore = new InMemoryEventStore();
        var profileResolver = new CountingProfileResolver();
        var pcs = BuildPcs(engine, eventStore, profileResolver, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId),
            previousFlexBalance: 0m);

        Assert.True(outcome.Result.Success);
        Assert.Null(outcome.Result.ErrorMessage);
        Assert.Empty(outcome.Result.RuleResults);
        Assert.Empty(outcome.Result.ExportLines);
        Assert.Empty(engine.Calls);
        Assert.Empty(profileResolver.AsOfDates);
        // S145 R-12: no EMPLOYED segment means nothing was evaluated under any agreement, so the result
        // reports none (empty), not the caller's agreement.
        Assert.Equal(string.Empty, outcome.Result.AgreementCode);

        // Manifest emission was attempted and the event half landed (not NoManifest).
        Assert.NotEqual(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);
        Assert.Equal(plan.ManifestId, outcome.ManifestId);
        var manifest = eventStore.Manifest;
        Assert.NotNull(manifest);
        Assert.Equal(plan.ManifestId, manifest!.ManifestId);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, Assert.Single(manifest.Segments).EmploymentStatus);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Codex-B1 / ADR-040 D10 — skip BEFORE profile resolution
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>The pre-hire-500 path is dead: for a starter plan, the profile resolver is
    /// asked ONLY about the employed segment's start date — never about the pre-hire span.
    /// Drives the FULL legacy shim (window hydration → typed plan → skip) through a fake
    /// window resolver, i.e. the exact production chain a hired-mid-month employee takes.</summary>
    [Fact]
    public async Task Shim_PreHireSegment_NeverResolvesProfile_EmployedSegmentDoes()
    {
        var hire = new DateOnly(2026, 3, 10);
        var windowResolver = new FakeWindowResolver(new EmploymentWindow(hire, null));
        var profileResolver = new CountingProfileResolver();
        var engine = new RecordingRuleEngine();
        var eventStore = new InMemoryEventStore();
        var pcs = BuildPcs(engine, eventStore, profileResolver, windowResolver, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var outcome = await pcs.CalculateWithOutcomeAsync(
            Profile(EmployeeId),
            Mar01, Mar31, previousFlexBalance: 0m);
#pragma warning restore CS0618

        Assert.True(outcome.Result.Success);

        // D10: exactly one profile lookup, at the hire date — none at 03-01 (pre-hire).
        Assert.Equal(new[] { hire }, profileResolver.AsOfDates);

        // D7: the window was read server-side for exactly the calculation period.
        Assert.Equal((EmployeeId, Mar01, Mar31), Assert.Single(windowResolver.Calls));

        // Rule engine only saw the employed span; the manifest carries the typed prefix.
        // S145 F1: per-segment calls start at the hire date; the timeline's pieces do too.
        Assert.All(engine.PerSegmentCalls, c => Assert.Equal(hire, c.PeriodStart));
        Assert.Equal(hire, Assert.Single(engine.TimelineCalls).Pieces![0].From);
        var manifest = eventStore.Manifest;
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Segments.Count);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, manifest.Segments[0].EmploymentStatus);
        Assert.Equal(hire.AddDays(-1), manifest.Segments[0].EndDate);
        Assert.Equal(BoundaryCause.EmploymentStarted, manifest.Segments[1].BoundaryCause);
    }

    // ═════════════════════════════════════════════════════════════════════
    // The opening flex balance reaches the ONE timeline call; a NOT_EMPLOYED
    // middle span is never covered (S145 F1 — the per-segment carry is retired)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Two spells (the planner is list-shaped/spells-proof) give EMPLOYED /
    /// NOT_EMPLOYED / EMPLOYED. S145 F1 retired the per-segment flex carry: there is no flex call
    /// per segment any more, and the opening balance goes to the plan's ONE timeline call, which
    /// carries it across the whole month itself. What the old pin protected survives in this form:
    /// the opening balance reaches the engine unchanged (not reset, not shifted by the skipped
    /// span); no flex call and no per-segment call touches the NOT_EMPLOYED span; the timeline's
    /// pieces and entries leave that span out (so no day of it is judged or paid); and no row is
    /// synthesized for it. The stub's flex delta is zero (no wage-type mapping exists in this
    /// DB-free harness — see the fixture doc), so a non-zero carry is the Docker-gated
    /// <c>EmploymentWindowPayrollTests</c>.</summary>
    [Fact]
    public async Task FlexCarry_PassesThroughNotEmployedMiddleSegment_Unchanged()
    {
        var firstSpellEnd = new DateOnly(2026, 3, 10);
        var secondSpellStart = new DateOnly(2026, 3, 21);
        var plan = Plan(EmployeeId, Mar01, Mar31, new[]
        {
            new EmploymentWindow(null, firstSpellEnd),
            new EmploymentWindow(secondSpellStart, null),
        });
        Assert.Equal(3, plan.Segments.Count);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[0].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[1].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[2].EmploymentStatus);

        const decimal openingBalance = 12.5m;
        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId),
            previousFlexBalance: openingBalance);

        Assert.True(outcome.Result.Success);

        // S145 R14 / F1: the per-segment flex call is retired — none at all.
        Assert.Empty(engine.FlexCalls);

        // One timeline call carries the opening balance, unchanged.
        var timeline = Assert.Single(engine.TimelineCalls);
        Assert.Equal(openingBalance, timeline.TimelinePreviousFlexBalance);

        // Its pieces cover 23 Feb (March's opening Monday) to 10 Mar, then 21 Mar to 31 Mar — and
        // nothing in between: the NOT_EMPLOYED span is never judged.
        Assert.Equal(new DateOnly(2026, 2, 23), timeline.Pieces![0].From);
        Assert.Equal(Mar31, timeline.Pieces[^1].To);
        Assert.Contains(timeline.Pieces, p => p.To == firstSpellEnd);
        Assert.Contains(timeline.Pieces, p => p.From == secondSpellStart);
        Assert.DoesNotContain(timeline.Pieces, p => p.From < secondSpellStart && p.To > firstSpellEnd);
        Assert.NotEmpty(timeline.TimelineEntries!); // S145 Step 5a: not vacuous
        Assert.DoesNotContain(timeline.TimelineEntries!, e => e.Date > firstSpellEnd && e.Date < secondSpellStart);

        // No per-segment call touched the NOT_EMPLOYED span either.
        Assert.DoesNotContain(engine.PerSegmentCalls, c => c.PeriodStart > firstSpellEnd && c.PeriodStart < secondSpellStart);
        // S145 R14: 1 timeline call + 3 per-segment calls for each of the 2 employed segments = 7 (was 12).
        Assert.Equal(TimelineCallsPerPlan + 2 * PerSegmentCallsPerEmployedSegment, engine.Calls.Count);

        // No synthesized rows: 6 rule ids, each merged from the two employed segments only.
        Assert.Equal(RuleResultsPerPlan, outcome.Result.RuleResults.Count);
        Assert.Equal(RuleResultsPerPlan, outcome.Result.RuleResults.Select(r => r.RuleId).Distinct().Count());
    }
}
