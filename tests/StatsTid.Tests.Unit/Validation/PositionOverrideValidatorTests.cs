using StatsTid.Backend.Api.Validation;

namespace StatsTid.Tests.Unit.Validation;

/// <summary>
/// SEC-033 — per-field range validation for the position-override write surfaces. Every field is
/// NULLABLE; null means "don't override" and is skipped, never rejected. Bounds are non-negativity
/// + the shared valid norm-period set ONLY — deliberately NO upper ceiling (200h is a real seeded
/// flex value). The endpoint 400 wiring is proven by the Docker-backed
/// <c>SEC033AdminConfigValidationTests</c>.
/// </summary>
public class PositionOverrideValidatorTests
{
    [Fact]
    public void AllNull_IsValid_NothingOverridden()
        => Assert.True(PositionOverrideValidator.ValidateRanges(null, null, null, null).IsValid);

    [Fact]
    public void FullyValidSuppliedValues_IsValid()
        => Assert.True(PositionOverrideValidator.ValidateRanges(37.0m, 4, 200.0m, 100.0m).IsValid);

    // ── WeeklyNormHours (when supplied, > 0) ──
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveWeeklyNormHours_IsRejected(double weekly)
    {
        var (valid, error) = PositionOverrideValidator.ValidateRanges((decimal)weekly, null, null, null);
        Assert.False(valid);
        Assert.Contains("WeeklyNormHours", error);
    }

    [Fact]
    public void NullWeeklyNormHours_IsSkipped()
        => Assert.True(PositionOverrideValidator.ValidateRanges(null, null, null, null).IsValid);

    // ── NormPeriodWeeks (when supplied, ∈ the shared valid set) ──
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(52)]
    public void OutOfSetNormPeriodWeeks_IsRejected(int weeks)
    {
        var (valid, error) = PositionOverrideValidator.ValidateRanges(null, weeks, null, null);
        Assert.False(valid);
        Assert.Contains("NormPeriodWeeks", error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void InSetNormPeriodWeeks_IsValid(int weeks)
        => Assert.True(PositionOverrideValidator.ValidateRanges(null, weeks, null, null).IsValid);

    // ── MaxFlexBalance / FlexCarryoverMax (when supplied, >= 0; NO upper cap) ──
    [Fact]
    public void NegativeMaxFlexBalance_IsRejected()
    {
        var (valid, error) = PositionOverrideValidator.ValidateRanges(null, null, -1m, null);
        Assert.False(valid);
        Assert.Contains("MaxFlexBalance", error);
    }

    [Fact]
    public void NegativeFlexCarryoverMax_IsRejected()
    {
        var (valid, error) = PositionOverrideValidator.ValidateRanges(null, null, null, -1m);
        Assert.False(valid);
        Assert.Contains("FlexCarryoverMax", error);
    }

    [Fact]
    public void ZeroFlexValues_AreAllowed()
        => Assert.True(PositionOverrideValidator.ValidateRanges(null, null, 0m, 0m).IsValid);

    [Fact]
    public void LargeFlexValue_IsAllowed_NoUpperCeiling()
    {
        // 200h is a real seeded flex value (AC DEPARTMENT_HEAD) — must NOT be capped.
        Assert.True(PositionOverrideValidator.ValidateRanges(null, null, 200.0m, 200.0m).IsValid);
    }
}
