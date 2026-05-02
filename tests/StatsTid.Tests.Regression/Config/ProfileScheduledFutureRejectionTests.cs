using StatsTid.Backend.Api.Validators;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// D11 fixture #15 — pins ADR-017 D2's "no scheduled future" rule for profile saves: an
/// effective_from in the future MUST surface a structured FieldValidationError with code
/// <c>EFFECTIVE_FROM_NOT_TODAY_OR_PAST</c>. The endpoint (<c>ConfigEndpoints.MapPut</c>)
/// calls <see cref="ProfileAlignmentValidator.ValidateEffectiveFromTemporality"/> and maps
/// a non-null result to HTTP 400. This test exercises the helper directly so a regression
/// in the predicate can never reach production.
/// </summary>
public sealed class ProfileScheduledFutureRejectionTests
{
    [Fact]
    public void EffectiveFromInFuture_Returns400()
    {
        var today = new DateOnly(2026, 5, 2);
        var future = today.AddDays(60);

        var error = ProfileAlignmentValidator.ValidateEffectiveFromTemporality(future, today);

        Assert.NotNull(error);
        Assert.Equal("EffectiveFrom", error!.Field);
        Assert.Equal("EFFECTIVE_FROM_NOT_TODAY_OR_PAST", error.Code);
        Assert.NotNull(error.NearestValid);
        Assert.Single(error.NearestValid!);
        Assert.Equal(today, error.NearestValid![0]);
    }

    [Fact]
    public void EffectiveFromIsToday_PassesWithoutError()
    {
        var today = new DateOnly(2026, 5, 2);

        var error = ProfileAlignmentValidator.ValidateEffectiveFromTemporality(today, today);

        Assert.Null(error);
    }

    [Fact]
    public void EffectiveFromInPast_PassesWithoutError()
    {
        var today = new DateOnly(2026, 5, 2);
        var past = today.AddDays(-30);

        var error = ProfileAlignmentValidator.ValidateEffectiveFromTemporality(past, today);

        Assert.Null(error);
    }
}
