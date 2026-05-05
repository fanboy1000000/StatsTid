using System.Text.Json;
using StatsTid.Backend.Api.Validators;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Config;

/// <summary>
/// D11 fixture #13 — pure unit test for <see cref="ProfileAlignmentValidator"/>'s structured
/// per-field error contract (ADR-017 D9a). The validator is the single source of truth for
/// save-time alignment checks; the PUT endpoint maps a failing
/// <see cref="ValidationResult"/> to HTTP 400 with the structured body documented in the ADR.
///
/// <para>
/// Scenario: a profile with <c>effective_from</c> on a Wednesday and a changed
/// <c>WeeklyNormHours</c> field — must fail with <c>NOT_MONDAY_ALIGNED</c> and the previous
/// + next Monday in <c>NearestValid</c>.
/// </para>
/// </summary>
public sealed class ProfileAlignmentValidatorTests
{
    [Fact]
    public void WeeklyNormHoursMidWeek_Returns400WithStructuredError()
    {
        var validator = new ProfileAlignmentValidator();

        // 2026-05-06 is a Wednesday. Previous Monday: 2026-05-04, next: 2026-05-11.
        var midWeek = new DateOnly(2026, 5, 6);
        Assert.Equal(DayOfWeek.Wednesday, midWeek.DayOfWeek);

        var profile = new LocalAgreementProfile
        {
            ProfileId = Guid.NewGuid(),
            OrgId = "STY02",
            AgreementCode = "HK",
            OkVersion = "OK24",
            EffectiveFrom = midWeek,
            WeeklyNormHours = 36m,
            CreatedBy = "admin1",
            CreatedAt = DateTime.UtcNow,
            Version = 1,
        };

        // ChangedFields keyed by field name, value is the new JsonElement (the validator
        // only cares about the keys to look up alignment policies).
        var changedFields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["WeeklyNormHours"] = JsonSerializer.SerializeToElement(36m),
        };

        var result = validator.Validate(profile, changedFields);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("WeeklyNormHours", error.Field);
        Assert.Equal("NOT_MONDAY_ALIGNED", error.Code);
        Assert.NotNull(error.NearestValid);
        Assert.Equal(2, error.NearestValid!.Count);
        Assert.Equal(new DateOnly(2026, 5, 4), error.NearestValid[0]);
        Assert.Equal(new DateOnly(2026, 5, 11), error.NearestValid[1]);
    }
}
