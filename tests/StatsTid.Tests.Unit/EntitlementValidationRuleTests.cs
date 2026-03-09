using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;

namespace StatsTid.Tests.Unit;

/// <summary>
/// Tests for EntitlementValidationRule (Sprint 15).
/// Verifies ALLOWED, WARNING, REJECTED states, pro-rating, per-episode limits,
/// carryover, and boundary conditions.
/// </summary>
public class EntitlementValidationRuleTests
{
    private static ValidateEntitlementRequest CreateRequest(
        decimal annualQuota = 25m,
        decimal used = 0m,
        decimal planned = 0m,
        decimal carryoverIn = 0m,
        decimal requestedDays = 1m,
        decimal partTimeFraction = 1.0m,
        bool proRateByPartTime = false,
        bool isPerEpisode = false,
        decimal? perEpisodeLimit = null) => new()
    {
        AnnualQuota = annualQuota,
        Used = used,
        Planned = planned,
        CarryoverIn = carryoverIn,
        RequestedDays = requestedDays,
        PartTimeFraction = partTimeFraction,
        ProRateByPartTime = proRateByPartTime,
        IsPerEpisode = isPerEpisode,
        PerEpisodeLimit = perEpisodeLimit,
    };

    [Fact]
    public void Evaluate_Request5Of25Quota_0Used_ReturnsAllowed_Remaining20()
    {
        var request = CreateRequest(annualQuota: 25m, used: 0m, requestedDays: 5m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal("ALLOWED", result.Status);
        Assert.Equal(20m, result.RemainingAfter);
        Assert.Equal(25m, result.EffectiveQuota);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Evaluate_Request1Of25Quota_20Used_ReturnsAllowed_AboveWarningThreshold()
    {
        // remaining after = 25 - 20 - 1 = 4, threshold = 25 * 0.2 = 5, 4 <= 5 → WARNING
        // Actually 4 <= 5 so this is WARNING. Let me adjust to test ALLOWED:
        // For ALLOWED: remaining must be > threshold. 25*0.2=5. Need remainingAfter > 5.
        // 25 - 18 - 1 = 6 > 5 → ALLOWED
        var request = CreateRequest(annualQuota: 25m, used: 18m, requestedDays: 1m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal("ALLOWED", result.Status);
        Assert.Equal(6m, result.RemainingAfter);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Evaluate_Request1Of25Quota_21Used_ReturnsWarning_BelowThreshold()
    {
        // remaining after = 25 - 21 - 1 = 3, threshold = 25 * 0.2 = 5, 3 <= 5 → WARNING
        var request = CreateRequest(annualQuota: 25m, used: 21m, requestedDays: 1m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal("WARNING", result.Status);
        Assert.Equal(3m, result.RemainingAfter);
        Assert.NotNull(result.Message);
        Assert.Contains("3", result.Message);
    }

    [Fact]
    public void Evaluate_Request1Of5Quota_3Used_ReturnsWarning()
    {
        // remaining after = 5 - 3 - 1 = 1, threshold = 5 * 0.2 = 1, 1 <= 1 → WARNING
        var request = CreateRequest(annualQuota: 5m, used: 3m, requestedDays: 1m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal("WARNING", result.Status);
        Assert.Equal(1m, result.RemainingAfter);
    }

    [Fact]
    public void Evaluate_Request5Of25Quota_22Used_ReturnsRejected()
    {
        // remaining after = 25 - 22 - 5 = -2 → REJECTED
        var request = CreateRequest(annualQuota: 25m, used: 22m, requestedDays: 5m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.False(result.Allowed);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(-2m, result.RemainingAfter);
        Assert.NotNull(result.Message);
        Assert.Contains("exceeds", result.Message);
    }

    [Fact]
    public void Evaluate_Request1Of2Quota_2Used_ReturnsRejected()
    {
        // remaining after = 2 - 2 - 1 = -1 → REJECTED
        var request = CreateRequest(annualQuota: 2m, used: 2m, requestedDays: 1m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.False(result.Allowed);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(-1m, result.RemainingAfter);
    }

    [Fact]
    public void Evaluate_Carryover_ExtendsAvailableQuota()
    {
        // 25 quota + 3 carryover = 28 available, 25 used, request 2 → remaining = 28 - 25 - 2 = 1
        // threshold = 25 * 0.2 = 5, 1 <= 5 → WARNING (allowed)
        var request = CreateRequest(annualQuota: 25m, carryoverIn: 3m, used: 25m, requestedDays: 2m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal(1m, result.RemainingAfter);
        Assert.Equal(25m, result.EffectiveQuota); // proRate off, so quota = 25
    }

    [Fact]
    public void Evaluate_PartTimeProRate_ReducesEffectiveQuota()
    {
        // 25 * 0.8 = 20 effective, 0 used, request 21 → remaining = 20 - 0 - 21 = -1 → REJECTED
        var request = CreateRequest(
            annualQuota: 25m, partTimeFraction: 0.8m, proRateByPartTime: true,
            requestedDays: 21m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.False(result.Allowed);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(20m, result.EffectiveQuota);
        Assert.Equal(-1m, result.RemainingAfter);
    }

    [Fact]
    public void Evaluate_PartTimeProRateDisabled_UsesFullQuota()
    {
        // proRate = false, so effective = 25 (not 25 * 0.8 = 20)
        var request = CreateRequest(
            annualQuota: 25m, partTimeFraction: 0.8m, proRateByPartTime: false,
            requestedDays: 21m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal(25m, result.EffectiveQuota);
        Assert.Equal(4m, result.RemainingAfter);
    }

    [Fact]
    public void Evaluate_PerEpisode_WithinLimit_ReturnsAllowed()
    {
        var request = CreateRequest(
            annualQuota: 100m, isPerEpisode: true, perEpisodeLimit: 2m,
            requestedDays: 2m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal("ALLOWED", result.Status);
        Assert.Equal(2m, result.EffectiveQuota);
        Assert.Equal(0m, result.RemainingAfter);
    }

    [Fact]
    public void Evaluate_PerEpisode_ExceedsLimit_ReturnsRejected()
    {
        var request = CreateRequest(
            annualQuota: 100m, isPerEpisode: true, perEpisodeLimit: 1m,
            requestedDays: 2m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.False(result.Allowed);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(1m, result.EffectiveQuota);
        Assert.Equal(-1m, result.RemainingAfter);
        Assert.Contains("per-episode limit", result.Message);
    }

    [Fact]
    public void Evaluate_PerEpisode_IgnoresAnnualQuotaAndUsed()
    {
        // annualQuota=100, limit=3, request 4 → REJECTED (uses per-episode limit, not annual)
        var request = CreateRequest(
            annualQuota: 100m, used: 50m, isPerEpisode: true, perEpisodeLimit: 3m,
            requestedDays: 4m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.False(result.Allowed);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(3m, result.EffectiveQuota);
    }

    [Fact]
    public void Evaluate_ZeroQuota_Request1_ReturnsRejected()
    {
        // SENIOR_DAY scenario: 0 quota, request 1 → remaining = 0 - 0 - 1 = -1 → REJECTED
        var request = CreateRequest(annualQuota: 0m, requestedDays: 1m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.False(result.Allowed);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(-1m, result.RemainingAfter);
    }

    [Fact]
    public void Evaluate_ExactBoundary_RequestExactlyRemaining_ReturnsWarning()
    {
        // 25 quota, 20 used, request 5 → remaining = 0
        // threshold = 25 * 0.2 = 5, 0 <= 5 → WARNING
        var request = CreateRequest(annualQuota: 25m, used: 20m, requestedDays: 5m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal("WARNING", result.Status);
        Assert.Equal(0m, result.RemainingAfter);
    }

    [Fact]
    public void Evaluate_LargeRequest_ExceedsQuota_ReturnsRejected()
    {
        // request 30 of 25 → remaining = 25 - 0 - 30 = -5 → REJECTED
        var request = CreateRequest(annualQuota: 25m, requestedDays: 30m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.False(result.Allowed);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(-5m, result.RemainingAfter);
    }

    [Fact]
    public void Evaluate_PlannedDaysReduceAvailable()
    {
        // 25 quota, 10 used, 10 planned, request 4 → remaining = 25 - 10 - 10 - 4 = 1
        // threshold = 25 * 0.2 = 5, 1 <= 5 → WARNING
        var request = CreateRequest(annualQuota: 25m, used: 10m, planned: 10m, requestedDays: 4m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal("WARNING", result.Status);
        Assert.Equal(1m, result.RemainingAfter);
    }

    [Theory]
    [InlineData(25, 0, 0, 0, 1, "ALLOWED")]   // 24 remaining > 5 threshold
    [InlineData(25, 20, 0, 0, 1, "WARNING")]   // 4 remaining <= 5 threshold
    [InlineData(25, 24, 0, 0, 1, "WARNING")]   // 0 remaining <= 5 threshold
    [InlineData(25, 25, 0, 0, 1, "REJECTED")]  // -1 remaining
    [InlineData(25, 0, 0, 5, 30, "WARNING")]   // 0 remaining (30 of 30)
    [InlineData(10, 0, 0, 0, 5, "ALLOWED")]    // 5 remaining > threshold 2
    public void Evaluate_Theory_VariousScenarios(
        decimal quota, decimal used, decimal planned, decimal carryover,
        decimal requested, string expectedStatus)
    {
        var request = CreateRequest(
            annualQuota: quota, used: used, planned: planned,
            carryoverIn: carryover, requestedDays: requested);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.Equal(expectedStatus, result.Status);
    }

    [Fact]
    public void Evaluate_PartTimeProRate_WarningThresholdUsesEffectiveQuota()
    {
        // 25 * 0.5 = 12.5 effective, threshold = 12.5 * 0.2 = 2.5
        // request 10 → remaining = 12.5 - 0 - 10 = 2.5, 2.5 <= 2.5 → WARNING
        var request = CreateRequest(
            annualQuota: 25m, partTimeFraction: 0.5m, proRateByPartTime: true,
            requestedDays: 10m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal("WARNING", result.Status);
        Assert.Equal(2.5m, result.RemainingAfter);
        Assert.Equal(12.5m, result.EffectiveQuota);
    }
}
