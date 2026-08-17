using StatsTid.Backend.Api.Validation;

namespace StatsTid.Tests.Unit.Validation;

/// <summary>
/// SEC-033 — per-field range validation for the entitlement-config write surfaces. These are the
/// pure-validator equivalents of the endpoint 400 negatives (the endpoint wiring is proven by the
/// Docker-backed <c>SEC033AdminConfigValidationTests</c>). Bounds are non-negativity + a calendar
/// month range ONLY — no invented upper ceilings.
/// </summary>
public class EntitlementConfigValidatorTests
{
    // A representative fully-valid, non-VACATION config (the positive control).
    private static (bool IsValid, string? Error) ValidateValid(
        string type = "CHILD_SICK", decimal annualQuota = 3m, decimal carryoverMax = 0m,
        int resetMonth = 1, int? minAge = null)
        => EntitlementConfigValidator.ValidateRanges(type, annualQuota, carryoverMax, resetMonth, minAge);

    [Fact]
    public void FullyValidConfig_IsValid()
    {
        var (valid, error) = ValidateValid();
        Assert.True(valid);
        Assert.Null(error);
    }

    // ── AnnualQuota ──
    [Fact]
    public void NegativeAnnualQuota_IsRejected()
    {
        var (valid, error) = ValidateValid(annualQuota: -1m);
        Assert.False(valid);
        Assert.Contains("AnnualQuota", error);
    }

    [Fact]
    public void ZeroAnnualQuota_IsAllowed()
        => Assert.True(ValidateValid(annualQuota: 0m).IsValid);

    // ── CarryoverMax (0 MUST be allowed) ──
    [Fact]
    public void NegativeCarryoverMax_IsRejected()
    {
        var (valid, error) = ValidateValid(carryoverMax: -0.5m);
        Assert.False(valid);
        Assert.Contains("CarryoverMax", error);
    }

    [Fact]
    public void ZeroCarryoverMax_IsAllowed_MustNotReject()
        => Assert.True(ValidateValid(carryoverMax: 0m).IsValid);

    // ── ResetMonth (non-VACATION → 1..12) ──
    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void OutOfRangeResetMonth_NonVacation_IsRejected(int resetMonth)
    {
        var (valid, error) = ValidateValid(resetMonth: resetMonth);
        Assert.False(valid);
        Assert.Contains("ResetMonth", error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(12)]
    public void InRangeResetMonth_NonVacation_IsValid(int resetMonth)
        => Assert.True(ValidateValid(resetMonth: resetMonth).IsValid);

    [Fact]
    public void VacationResetMonth_IsExemptFromRangeCheck_DeferredToStatutory422Guard()
    {
        // For VACATION the reset_month is a statutory constant (9), enforced by the endpoint's own
        // 422 guard + a DB CHECK — NOT by this 400 range layer. So even an out-of-1..12 value is
        // "valid" here (the endpoint's 422 guard is what rejects a non-9 VACATION month).
        Assert.True(EntitlementConfigValidator.ValidateRanges("VACATION", 25m, 5m, 13, null).IsValid);
    }

    // ── MinAge (nullable — skip when null) ──
    [Fact]
    public void NullMinAge_IsAllowed_Skipped()
        => Assert.True(ValidateValid(minAge: null).IsValid);

    [Fact]
    public void ZeroMinAge_IsAllowed()
        => Assert.True(ValidateValid(minAge: 0).IsValid);

    [Fact]
    public void NegativeMinAge_IsRejected()
    {
        var (valid, error) = ValidateValid(minAge: -1);
        Assert.False(valid);
        Assert.Contains("MinAge", error);
    }
}
