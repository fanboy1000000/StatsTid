using StatsTid.Backend.Api.Endpoints;
using StatsTid.Infrastructure;

namespace StatsTid.Tests.Unit.Settlement;

/// <summary>
/// S71 / TASK-7104 (SPRINT-71 R4 — the ONE-lifecycle-write-implementation rule).
/// <see cref="EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle"/> is the CANONICAL host
/// of the R1 deactivation-lifecycle decision. <b>The TASK-7102 refactor LANDED:</b>
/// <see cref="EmploymentDateEndpoints.ComputeEndDateLifecycle"/> is now a THIN DELEGATION to
/// the writer (the S70 transitional duplicate implementation is deleted), so the grid parity
/// below is by-construction — it remains as the regression tripwire against a future
/// re-divergence (someone re-inlining a second implementation behind the public symbol). The
/// spot-pins anchor the CANONICAL R1 outcomes to the pinned semantics, not merely to mutual
/// agreement. The per-clause behavioral suite for the decision itself lives in
/// <c>Endpoints/EmploymentEndDateLifecycleLogicTests</c> (S70, now also exercising the
/// canonical implementation through the delegating symbol).
///
/// <para><b>Scope boundary (Step-5a cycle-1 NOTE):</b> this suite pins ONLY the pure decision.
/// The writer's full WRITE-EFFECT set (versioned user write, the R10 event payload incl. the
/// version pair, the users_audit row shape, the R1(e) reporting-line emission on a
/// deactivating flip) is pinned directly by the regression suite
/// <c>Settlement/EndDateLifecycleWriterEffectTests</c>, and the refactored PUT's end-to-end
/// behavior by the S70 <c>Settlement/EmploymentEndDateLifecycleTests</c> suite.</para>
/// </summary>
public class EndDateLifecycleWriterParityTests
{
    private static readonly DateOnly Today = new(2026, 3, 5);

    /// <summary>The end-date axis: null (clear), long-passed, passed-by-one, == today (the last
    /// EMPLOYED day — not passed), tomorrow, far-future.</summary>
    public static IEnumerable<object?[]> FullGrid()
    {
        var endDates = new DateOnly?[]
        {
            null,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 4),
            new DateOnly(2026, 3, 5),
            new DateOnly(2026, 3, 6),
            new DateOnly(2026, 12, 31),
        };
        foreach (var endDate in endDates)
        foreach (var oldIsActive in new[] { true, false })
        foreach (var oldEndDateDeactivated in new[] { true, false })
            yield return [endDate, oldIsActive, oldEndDateDeactivated];
    }

    [Theory]
    [MemberData(nameof(FullGrid))]
    public void WriterDecision_IsIdenticalToEndpointDecision_OverTheFullGrid(
        DateOnly? newEndDate, bool oldIsActive, bool oldEndDateDeactivated)
    {
        var endpoint = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            newEndDate, oldIsActive, oldEndDateDeactivated, Today);
        var writer = EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle(
            newEndDate, oldIsActive, oldEndDateDeactivated, Today);

        Assert.Equal(endpoint, writer);
    }

    // ─────────────── canonical R1 spot-pins (anchor the parity to the pinned semantics) ───────────────

    /// <summary>R1(a) — set already-passed on an active row ⇒ same-tx deactivate with provenance.</summary>
    [Fact]
    public void SetPassed_ActiveRow_DeactivatesWithProvenance()
    {
        Assert.Equal((false, true), EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle(
            new DateOnly(2026, 3, 4), oldIsActive: true, oldEndDateDeactivated: false, Today));
    }

    /// <summary>R1(b) — future-dated (incl. == today, still the last EMPLOYED day) ⇒ no flip.</summary>
    [Fact]
    public void SetTodayOrFuture_ActiveRow_NoFlip()
    {
        Assert.Equal((true, false), EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle(
            Today, true, false, Today));
        Assert.Equal((true, false), EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle(
            new DateOnly(2026, 12, 31), true, false, Today));
    }

    /// <summary>R1(c) — clear reactivates ONLY on lifecycle provenance (then resets it).</summary>
    [Fact]
    public void Clear_LifecycleDeactivatedRow_Reactivates()
    {
        Assert.Equal((true, false), EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle(
            null, oldIsActive: false, oldEndDateDeactivated: true, Today));
    }

    /// <summary>R1(c) — clear on a MANUALLY-deactivated row clears only (no reactivation).</summary>
    [Fact]
    public void Clear_ManuallyDeactivatedRow_StaysInactive()
    {
        Assert.Equal((false, false), EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle(
            null, false, false, Today));
    }

    /// <summary>R1(d) — set on a manually-inactive row records the date, claims NO provenance.</summary>
    [Fact]
    public void SetPassed_ManuallyInactiveRow_NoProvenanceClaim()
    {
        Assert.Equal((false, false), EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle(
            new DateOnly(2026, 1, 1), false, false, Today));
    }

    /// <summary>Correction on a lifecycle-deactivated row — deterministic re-evaluation:
    /// still-passed stays deactivated; corrected-to-unpassed REACTIVATES + resets provenance
    /// (the S70 TASK-7002 accepted deviation; the supersede-as-YEAR_END enabler).</summary>
    [Fact]
    public void Correction_LifecycleDeactivatedRow_ReEvaluates()
    {
        Assert.Equal((false, true), EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle(
            new DateOnly(2025, 12, 31), false, true, Today));
        Assert.Equal((true, false), EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle(
            new DateOnly(2026, 8, 15), false, true, Today));
    }
}
