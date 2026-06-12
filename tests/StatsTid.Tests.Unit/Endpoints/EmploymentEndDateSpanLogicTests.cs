using StatsTid.Backend.Api.Endpoints;

namespace StatsTid.Tests.Unit.Endpoints;

/// <summary>
/// S71 / TASK-7102 (SPRINT-71 R13) — the RANGE-WIDENED affected-ferieår span,
/// <see cref="EmploymentDateEndpoints.AffectedFerieaarSpan"/>: the R7a guard's year set widens
/// from the S70 {ferieår(old), ferieår(new)} PAIR to the FULL inclusive span
/// <c>[min..max]</c>, so a forward OR backward end-date correction crossing an INTERMEDIATE
/// settled ferieår is caught instead of silently bypassing the guard. The pair helper
/// (<see cref="EmploymentDateEndpoints.AffectedFerieaar"/>) keeps its own S70 suite
/// (<c>EmploymentEndDateLifecycleLogicTests</c>) — these tests pin where the two DIFFER.
/// </summary>
public class EmploymentEndDateSpanLogicTests
{
    [Fact]
    public void BothNull_Empty()
    {
        Assert.Empty(EmploymentDateEndpoints.AffectedFerieaarSpan(null, null));
    }

    [Fact]
    public void SetOnly_SingleYear_MatchesPairSemantics()
    {
        // ferieår(2026-06-30) = 2025 (month 6 < 9).
        var span = EmploymentDateEndpoints.AffectedFerieaarSpan(null, new DateOnly(2026, 6, 30));
        Assert.Equal([2025], span);
    }

    [Fact]
    public void ClearOnly_SingleYear_MatchesPairSemantics()
    {
        // ferieår(2026-10-01) = 2026 (month 10 >= 9).
        var span = EmploymentDateEndpoints.AffectedFerieaarSpan(new DateOnly(2026, 10, 1), null);
        Assert.Equal([2026], span);
    }

    [Fact]
    public void SameFerieaar_Correction_SingleYear()
    {
        // Both 2026-02-28 and 2025-12-31 fall in ferieår 2025.
        var span = EmploymentDateEndpoints.AffectedFerieaarSpan(
            new DateOnly(2026, 2, 28), new DateOnly(2025, 12, 31));
        Assert.Equal([2025], span);
    }

    /// <summary>The R13 headline, FORWARD: old 2024-06-15 (ferieår 2023) → new 2026-10-01
    /// (ferieår 2026) — the INTERMEDIATE years 2024 and 2025 are now included (the S70 pair
    /// missed them).</summary>
    [Fact]
    public void ForwardCorrection_AcrossYears_FullInclusiveSpan_InterveningYearsIncluded()
    {
        var old = new DateOnly(2024, 6, 15);
        var corrected = new DateOnly(2026, 10, 1);

        var span = EmploymentDateEndpoints.AffectedFerieaarSpan(old, corrected);

        Assert.Equal([2023, 2024, 2025, 2026], span);
        // The S70 pair would have missed the intermediates — pinned as the R13 delta.
        var pair = EmploymentDateEndpoints.AffectedFerieaar(old, corrected);
        Assert.DoesNotContain(2024, pair);
        Assert.DoesNotContain(2025, pair);
    }

    /// <summary>The R13 headline, BACKWARD (cycle-2 Codex N: BOTH directions pinned): the span
    /// is direction-independent — swapping old/new yields the identical year set.</summary>
    [Fact]
    public void BackwardCorrection_AcrossYears_SameSpanAsForward()
    {
        var forward = EmploymentDateEndpoints.AffectedFerieaarSpan(
            new DateOnly(2024, 6, 15), new DateOnly(2026, 10, 1));
        var backward = EmploymentDateEndpoints.AffectedFerieaarSpan(
            new DateOnly(2026, 10, 1), new DateOnly(2024, 6, 15));

        Assert.Equal(forward, backward);
        Assert.Equal([2023, 2024, 2025, 2026], backward);
    }

    [Fact]
    public void Span_IsAscending()
    {
        var span = EmploymentDateEndpoints.AffectedFerieaarSpan(
            new DateOnly(2027, 9, 1), new DateOnly(2023, 1, 15));
        Assert.Equal(span.OrderBy(y => y), span);
        Assert.Equal([2022, 2023, 2024, 2025, 2026, 2027], span);
    }
}
