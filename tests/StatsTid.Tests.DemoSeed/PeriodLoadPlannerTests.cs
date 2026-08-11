using StatsTid.Tools.DemoSeed.Loading;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S128 / TASK-12802 — the loader's PURE period-stage decision (S127 FU-C): probe-first
/// idempotency. The defining requirement is that a re-run over an already-loaded DB plans ZERO
/// period writes — EXPLICITLY including REJECTED months, which the pre-S128 conflict-tolerant
/// stage re-sent + re-rejected every run (REJECTED is a legitimate send source, so the send never
/// 409'd; final statuses were identical but each re-run appended a spurious send/reject event pair).
/// </summary>
public sealed class PeriodLoadPlannerTests
{
    private static DemoActivity Row(string outcome) => new()
    {
        EmployeeId = "e1",
        Year = 2026,
        Month = 5,
        PeriodOutcome = outcome,
    };

    private static PeriodLoadPlanner.ObservedPeriod Observed(string status)
        => new(Guid.NewGuid(), status);

    // ── (a) The re-run: observed == target ⇒ ZERO writes, including REJECTED ──

    [Theory]
    [InlineData("EMPLOYEE_APPROVED", "EMPLOYEE_APPROVED")]
    [InlineData("SUBMITTED", "EMPLOYEE_APPROVED")] // legacy outcome spelling, same target state
    [InlineData("APPROVED", "APPROVED")]
    [InlineData("REJECTED", "REJECTED")]
    public void Rerun_ObservedEqualsTarget_PlansZeroActions(string outcome, string observedStatus)
    {
        var plan = PeriodLoadPlanner.PlanPeriodActions(Row(outcome), Observed(observedStatus));

        Assert.Empty(plan.Steps);
        Assert.Equal(PeriodLoadPlanner.PeriodSkipReason.AlreadyInTargetState, plan.Skip);
    }

    [Fact]
    public void Rerun_RejectedMonth_PlansZeroActions_TheS127FuCCase()
    {
        // Pinned on its own, not only via the Theory: REJECTED is a legitimate send source
        // (AllowedSendSourceStates = {DRAFT, SUBMITTED, REJECTED}), so this is the one target state
        // the old 409-tolerance could NOT protect — the re-send succeeded, the re-reject followed,
        // and every re-run appended an extra send/reject event pair per rejected month.
        var plan = PeriodLoadPlanner.PlanPeriodActions(Row("REJECTED"), Observed("REJECTED"));

        Assert.Empty(plan.Steps);
        Assert.Equal(PeriodLoadPlanner.PeriodSkipReason.AlreadyInTargetState, plan.Skip);
    }

    // ── (b) The fresh load: no observed row ⇒ the full per-outcome sequence ──

    [Theory]
    [InlineData("EMPLOYEE_APPROVED", new[] { PeriodLoadPlanner.PeriodStep.Send })]
    [InlineData("SUBMITTED", new[] { PeriodLoadPlanner.PeriodStep.Send })] // legacy: send and stop
    [InlineData("APPROVED", new[] { PeriodLoadPlanner.PeriodStep.Send, PeriodLoadPlanner.PeriodStep.Approve })]
    [InlineData("REJECTED", new[] { PeriodLoadPlanner.PeriodStep.Send, PeriodLoadPlanner.PeriodStep.Reject })]
    public void FreshLoad_NoObservedRow_PlansTheFullSequence(string outcome, PeriodLoadPlanner.PeriodStep[] expected)
    {
        var plan = PeriodLoadPlanner.PlanPeriodActions(Row(outcome), observed: null);

        Assert.Equal(expected, plan.Steps);
        Assert.Equal(PeriodLoadPlanner.PeriodSkipReason.NotSkipped, plan.Skip);
    }

    // ── (c) The partial state: only the REMAINING action ──
    //
    // Supported directly by the current server semantics: approve AND reject both accept
    // EMPLOYEE_APPROVED as a source state (ApprovalEndpoints.cs allowedSourceStates =
    // {SUBMITTED, EMPLOYEE_APPROVED}), and the by-month probe supplies the period id to address.
    // A re-send here would be WORSE than unnecessary — EMPLOYEE_APPROVED is not a send source, so
    // the send would 409 and the pre-S128 loader aborted the row there, never applying the outcome.

    [Fact]
    public void Partial_ObservedEmployeeApproved_TargetApproved_PlansApproveOnly()
    {
        var plan = PeriodLoadPlanner.PlanPeriodActions(Row("APPROVED"), Observed("EMPLOYEE_APPROVED"));

        Assert.Equal(new[] { PeriodLoadPlanner.PeriodStep.Approve }, plan.Steps);
        Assert.Equal(PeriodLoadPlanner.PeriodSkipReason.NotSkipped, plan.Skip);
    }

    [Fact]
    public void Partial_ObservedEmployeeApproved_TargetRejected_PlansRejectOnly()
    {
        var plan = PeriodLoadPlanner.PlanPeriodActions(Row("REJECTED"), Observed("EMPLOYEE_APPROVED"));

        Assert.Equal(new[] { PeriodLoadPlanner.PeriodStep.Reject }, plan.Steps);
        Assert.Equal(PeriodLoadPlanner.PeriodSkipReason.NotSkipped, plan.Skip);
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("SUBMITTED")] // a legacy row: send is its ONLY route forward (and repairs dimensions)
    [InlineData("REJECTED")]
    public void Partial_ObservedSendableSource_TargetApproved_PlansSendThenApprove(string observedStatus)
    {
        // All three are legitimate send sources, so the full sequence is the correct (and cheapest
        // correct) plan — a direct approve is only defined from SUBMITTED/EMPLOYEE_APPROVED, and
        // for DRAFT/REJECTED the row must pass through the send anyway.
        var plan = PeriodLoadPlanner.PlanPeriodActions(Row("APPROVED"), Observed(observedStatus));

        Assert.Equal(new[] { PeriodLoadPlanner.PeriodStep.Send, PeriodLoadPlanner.PeriodStep.Approve }, plan.Steps);
    }

    [Fact]
    public void Partial_ObservedApproved_TargetRejected_FallsBackToTheFullSequence()
    {
        // APPROVED is locked (not a send/approve/reject source) — only a reopen could move it, and
        // the loader deliberately has no reopen. The HONEST plan is the pre-S128 full sequence: the
        // send 409s into the loader's PeriodsAlreadySent safety net, byte-for-byte the old behaviour
        // for this (manifest-drifted) row, rather than a silent "already done" that it is not.
        var plan = PeriodLoadPlanner.PlanPeriodActions(Row("REJECTED"), Observed("APPROVED"));

        Assert.Equal(new[] { PeriodLoadPlanner.PeriodStep.Send, PeriodLoadPlanner.PeriodStep.Reject }, plan.Steps);
        Assert.Equal(PeriodLoadPlanner.PeriodSkipReason.NotSkipped, plan.Skip);
    }

    // ── (d) NONE never enters the send stage, whatever the world looks like ──

    [Theory]
    [InlineData(null)]
    [InlineData("DRAFT")]
    [InlineData("SUBMITTED")]
    [InlineData("EMPLOYEE_APPROVED")]
    [InlineData("APPROVED")]
    [InlineData("REJECTED")]
    public void NoneOutcome_PlansNothing_InEveryObservedState(string? observedStatus)
    {
        var observed = observedStatus is null ? null : Observed(observedStatus);

        var plan = PeriodLoadPlanner.PlanPeriodActions(Row("NONE"), observed);

        Assert.Empty(plan.Steps);
        Assert.Equal(PeriodLoadPlanner.PeriodSkipReason.NoneOutcome, plan.Skip);
    }

    // ── The single-sourced outcome → end-status mapping (loader plans toward it, verifier checks
    //    the DB against it — one definition since S128) ──

    [Theory]
    [InlineData("NONE", null)]
    [InlineData("EMPLOYEE_APPROVED", "EMPLOYEE_APPROVED")]
    [InlineData("SUBMITTED", "EMPLOYEE_APPROVED")] // the pre-S127 spelling still tells the truth
    [InlineData("APPROVED", "APPROVED")]
    [InlineData("REJECTED", "REJECTED")]
    public void ExpectedPeriodStatus_MapsEveryKnownOutcome(string outcome, string? expected)
    {
        Assert.Equal(expected, PeriodLoadPlanner.ExpectedPeriodStatus(outcome));
    }

    [Fact]
    public void ExpectedPeriodStatus_UnknownOutcome_Throws_NeverSilentlyDropsMonths()
    {
        // A silent "no expectation" default is how a whole class of months would drop out of BOTH
        // the load plan and the verifier's status-count check.
        Assert.Throws<InvalidOperationException>(
            () => PeriodLoadPlanner.ExpectedPeriodStatus("GARBAGE"));
    }

    // ── AC-3 arm (a) analogue at planner level: a whole re-run dataset plans zero writes ──

    [Fact]
    public void Rerun_WholeDataset_EveryObservedAtTarget_PlansZeroWritesTotal()
    {
        // A miniature of the real manifest month: uniform outcome distribution over
        // NONE / EMPLOYEE_APPROVED / APPROVED / REJECTED (DemoGenerator), with the world observed
        // exactly as a completed first load leaves it.
        var outcomes = new[] { "NONE", "EMPLOYEE_APPROVED", "APPROVED", "REJECTED" };
        var rows = Enumerable.Range(0, 40)
            .Select(i => new DemoActivity
            {
                EmployeeId = $"e{i}",
                Year = 2026,
                Month = 5,
                PeriodOutcome = outcomes[i % outcomes.Length],
            })
            .ToList();

        var totalSteps = 0;
        var alreadyInTarget = 0;
        foreach (var row in rows)
        {
            var target = PeriodLoadPlanner.ExpectedPeriodStatus(row.PeriodOutcome);
            var observed = target is null ? null : Observed(target); // NONE rows have no row at all
            var plan = PeriodLoadPlanner.PlanPeriodActions(row, observed);
            totalSteps += plan.Steps.Count;
            if (plan.Skip == PeriodLoadPlanner.PeriodSkipReason.AlreadyInTargetState)
                alreadyInTarget++;
        }

        Assert.Equal(0, totalSteps); // the re-run is WRITE-FREE for the period stage
        Assert.Equal(rows.Count(r => r.PeriodOutcome != "NONE"), alreadyInTarget); // the AC-3(b) counter identity
    }

    // ── The EXHAUSTIVE mismatch matrix (S128 Step-7a Codex WARNING absorption) ──
    //
    // The sections above pinned the four load-bearing rows of the decision table (at-target,
    // fresh-load, EMPLOYEE_APPROVED-resume, NONE). The remaining outcome × observed-status
    // combinations all fall to the "full sequence" arm, and Step 7a flagged that leaving them
    // implicit lets future planner drift escape this suite: a sendable source with a different
    // target legitimately re-sends; a locked APPROVED row whose manifest target drifted re-sends
    // into the loader's 409 PeriodsAlreadySent safety net — byte-for-byte the pre-S128 behaviour.

    [Theory]
    // target EMPLOYEE_APPROVED (incl. the legacy "SUBMITTED" spelling) vs every non-target state:
    [InlineData("EMPLOYEE_APPROVED", "DRAFT", new[] { PeriodLoadPlanner.PeriodStep.Send })]
    [InlineData("EMPLOYEE_APPROVED", "SUBMITTED", new[] { PeriodLoadPlanner.PeriodStep.Send })]
    [InlineData("EMPLOYEE_APPROVED", "REJECTED", new[] { PeriodLoadPlanner.PeriodStep.Send })]
    [InlineData("EMPLOYEE_APPROVED", "APPROVED", new[] { PeriodLoadPlanner.PeriodStep.Send })] // locked drift → 409 net
    [InlineData("SUBMITTED", "DRAFT", new[] { PeriodLoadPlanner.PeriodStep.Send })]
    [InlineData("SUBMITTED", "SUBMITTED", new[] { PeriodLoadPlanner.PeriodStep.Send })] // legacy outcome ≠ legacy status
    [InlineData("SUBMITTED", "REJECTED", new[] { PeriodLoadPlanner.PeriodStep.Send })]
    [InlineData("SUBMITTED", "APPROVED", new[] { PeriodLoadPlanner.PeriodStep.Send })]
    // target REJECTED vs the sendable non-target sources:
    [InlineData("REJECTED", "DRAFT", new[] { PeriodLoadPlanner.PeriodStep.Send, PeriodLoadPlanner.PeriodStep.Reject })]
    [InlineData("REJECTED", "SUBMITTED", new[] { PeriodLoadPlanner.PeriodStep.Send, PeriodLoadPlanner.PeriodStep.Reject })]
    [InlineData("REJECTED", "APPROVED", new[] { PeriodLoadPlanner.PeriodStep.Send, PeriodLoadPlanner.PeriodStep.Reject })] // locked drift
    // target APPROVED vs the sendable non-target sources:
    [InlineData("APPROVED", "DRAFT", new[] { PeriodLoadPlanner.PeriodStep.Send, PeriodLoadPlanner.PeriodStep.Approve })]
    [InlineData("APPROVED", "SUBMITTED", new[] { PeriodLoadPlanner.PeriodStep.Send, PeriodLoadPlanner.PeriodStep.Approve })]
    [InlineData("APPROVED", "REJECTED", new[] { PeriodLoadPlanner.PeriodStep.Send, PeriodLoadPlanner.PeriodStep.Approve })]
    public void Mismatch_SendableOrDriftedState_PlansTheFullSequence(
        string outcome, string observedStatus, PeriodLoadPlanner.PeriodStep[] expected)
    {
        var plan = PeriodLoadPlanner.PlanPeriodActions(Row(outcome), Observed(observedStatus));

        Assert.Equal(expected, plan.Steps);
        Assert.Equal(PeriodLoadPlanner.PeriodSkipReason.NotSkipped, plan.Skip);
    }

    [Theory]
    // NONE stays inert against EVERY observed state, not only the seeded ones:
    [InlineData("DRAFT")]
    [InlineData("SUBMITTED")]
    [InlineData("EMPLOYEE_APPROVED")]
    [InlineData("APPROVED")]
    [InlineData("REJECTED")]
    public void NoneOutcome_IsInert_InEveryObservedState(string observedStatus)
    {
        var plan = PeriodLoadPlanner.PlanPeriodActions(Row("NONE"), Observed(observedStatus));

        Assert.Empty(plan.Steps);
        Assert.Equal(PeriodLoadPlanner.PeriodSkipReason.NoneOutcome, plan.Skip);
    }
}
