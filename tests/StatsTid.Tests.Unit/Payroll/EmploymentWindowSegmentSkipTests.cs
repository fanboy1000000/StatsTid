using StatsTid.Integrations.Payroll.Services;
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
///     has a zero budget, never short-circuits, and returns Success with no results, no
///     lines, and a manifest of all-NOT_EMPLOYED segments — the ruled semantics.</item>
///   <item><b>Flex carry passes THROUGH a skipped middle segment</b> unchanged (an empty
///     result list yields no flex delta, and no row is ever synthesized for it).</item>
/// </list>
/// </para>
/// </summary>
public sealed class EmploymentWindowSegmentSkipTests
{
    private const string EmployeeId = "EMP-S137-SKIP";

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
        var pcs = BuildPcs(engine);

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId), WeekdayEntries(EmployeeId, Mar01, Mar31),
            Array.Empty<AbsenceEntry>(), previousFlexBalance: 0m);

        Assert.False(outcome.Result.Success);
        Assert.Equal("All rule evaluations failed", outcome.Result.ErrorMessage);
        Assert.Equal(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);

        // Exactly one employed segment's worth of attempts, none for the pre-hire span.
        Assert.Equal(RuleCallsPerEmployedSegment, engine.Calls.Count);
        Assert.All(engine.Calls, c => Assert.Equal(hire, c.PeriodStart));
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
        var pcs = BuildPcs(engine);

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId), WeekdayEntries(EmployeeId, Mar01, Mar31),
            Array.Empty<AbsenceEntry>(), previousFlexBalance: 0m);

        Assert.False(outcome.Result.Success);
        Assert.Equal("All rule evaluations failed", outcome.Result.ErrorMessage);
        Assert.Empty(outcome.Result.ExportLines);
        Assert.Equal(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);
        Assert.Equal(RuleCallsPerEmployedSegment, engine.Calls.Count);
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
        var pcs = BuildPcs(engine, eventStore);

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId), WeekdayEntries(EmployeeId, Mar01, Mar31),
            Array.Empty<AbsenceEntry>(), previousFlexBalance: 0m);

        Assert.True(outcome.Result.Success);
        Assert.Null(outcome.Result.ErrorMessage);
        // One employed segment → one result per stubbed rule (4 time + absence + flex).
        Assert.Equal(RuleCallsPerEmployedSegment, outcome.Result.RuleResults.Count);
        Assert.All(outcome.Result.RuleResults, r => Assert.True(r.Success));
        Assert.NotEqual(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);

        // The rule engine was only ever asked about the employed span [03-01 .. 03-15].
        Assert.Equal(RuleCallsPerEmployedSegment, engine.Calls.Count);
        Assert.All(engine.Calls, c =>
        {
            Assert.Equal(Mar01, c.PeriodStart);
            Assert.Equal(lastDay, c.PeriodEnd); // the last employed day IS inside the evaluated range
        });

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
        var pcs = BuildPcs(engine, eventStore, profileResolver);

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId), WeekdayEntries(EmployeeId, Mar01, Mar31),
            Array.Empty<AbsenceEntry>(), previousFlexBalance: 0m);

        Assert.True(outcome.Result.Success);
        Assert.Null(outcome.Result.ErrorMessage);
        Assert.Empty(outcome.Result.RuleResults);
        Assert.Empty(outcome.Result.ExportLines);
        Assert.Empty(engine.Calls);
        Assert.Empty(profileResolver.AsOfDates);

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
        var pcs = BuildPcs(engine, eventStore, profileResolver, windowResolver);

#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var outcome = await pcs.CalculateWithOutcomeAsync(
            Profile(EmployeeId), WeekdayEntries(EmployeeId, Mar01, Mar31), Array.Empty<AbsenceEntry>(),
            Mar01, Mar31, previousFlexBalance: 0m);
#pragma warning restore CS0618

        Assert.True(outcome.Result.Success);

        // D10: exactly one profile lookup, at the hire date — none at 03-01 (pre-hire).
        Assert.Equal(new[] { hire }, profileResolver.AsOfDates);

        // D7: the window was read server-side for exactly the calculation period.
        Assert.Equal((EmployeeId, Mar01, Mar31), Assert.Single(windowResolver.Calls));

        // Rule engine only saw the employed span; the manifest carries the typed prefix.
        Assert.All(engine.Calls, c => Assert.Equal(hire, c.PeriodStart));
        var manifest = eventStore.Manifest;
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Segments.Count);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, manifest.Segments[0].EmploymentStatus);
        Assert.Equal(hire.AddDays(-1), manifest.Segments[0].EndDate);
        Assert.Equal(BoundaryCause.EmploymentStarted, manifest.Segments[1].BoundaryCause);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Flex carry passes THROUGH a NOT_EMPLOYED middle segment
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Two spells (the planner is list-shaped/spells-proof) give EMPLOYED /
    /// NOT_EMPLOYED / EMPLOYED. The third segment's flex call must receive exactly the carry
    /// the first segment produced; the middle segment issues no flex call and synthesizes
    /// no row. The stub's flex delta is zero (no wage-type mapping exists in this DB-free
    /// harness — see the fixture doc), so the pin is structural: a carry RESET on the skip
    /// (e.g. to 0) or a synthesized FLEX row would fail it; the non-zero-delta variant is
    /// the Docker-gated <c>EmploymentWindowPayrollTests</c>.</summary>
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
        var pcs = BuildPcs(engine);

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId), WeekdayEntries(EmployeeId, Mar01, Mar31),
            Array.Empty<AbsenceEntry>(), previousFlexBalance: openingBalance);

        Assert.True(outcome.Result.Success);

        // Exactly two flex calls — segments 0 and 2, in order; none for the middle span.
        var flexCalls = engine.FlexCalls;
        Assert.Equal(2, flexCalls.Count);
        Assert.Equal(Mar01, flexCalls[0].PeriodStart);
        Assert.Equal(firstSpellEnd, flexCalls[0].PeriodEnd);
        Assert.Equal(secondSpellStart, flexCalls[1].PeriodStart);
        Assert.Equal(Mar31, flexCalls[1].PeriodEnd);

        // The carry the third segment receives == the first segment's carry (+ its zero delta).
        Assert.Equal(openingBalance, flexCalls[0].PreviousBalance);
        Assert.Equal(openingBalance, flexCalls[1].PreviousBalance);

        // No call of any kind touched the NOT_EMPLOYED span.
        Assert.DoesNotContain(engine.Calls, c => c.PeriodStart > firstSpellEnd && c.PeriodStart < secondSpellStart);
        Assert.Equal(2 * RuleCallsPerEmployedSegment, engine.Calls.Count);

        // No synthesized rows: 6 rule ids, each merged from the two employed segments only.
        Assert.Equal(RuleCallsPerEmployedSegment, outcome.Result.RuleResults.Count);
        Assert.Equal(RuleCallsPerEmployedSegment, outcome.Result.RuleResults.Select(r => r.RuleId).Distinct().Count());
    }
}
