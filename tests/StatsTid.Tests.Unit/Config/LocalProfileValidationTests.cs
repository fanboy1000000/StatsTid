using System.Globalization;
using System.Text.Json;
using StatsTid.Backend.Api.Validators;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Config;

/// <summary>
/// S148 / TASK-14832-T (AC-C4b, AC-C4f, AC-C4g; ADR-043 D2, D3, D6; SEC-061) - the pure half of the local
/// profile's value check, through <see cref="ProfileAlignmentValidator.Validate"/>, the one entry point the
/// PUT route calls. The Docker half (the 400 and "nothing stored") has the same three names in
/// <c>StatsTid.Tests.Regression.Config.LocalProfileValidationTests</c>.
///
/// <para>
/// <b>In plain language.</b> A LocalAdmin saves an institution's own settings. A negative cap, a weekday window
/// that ends before it starts, a weekly norm of 0 and a number the database column could not hold exactly must
/// be refused with a named field and a code, while the column's own largest value and a number written with a
/// trailing zero must be accepted. Expected codes are literals from the ADR-043 / TASK-14832 contract, never
/// read from production constants.
/// </para>
///
/// <para>
/// Every profile is dated Monday 2 March 2026 and the changed-fields map is empty, so only the value rules can
/// refuse. <b>Kind F (Unit), all three theories.</b> Red conditions: before TASK-14832 <c>Validate</c> checks the
/// Monday alignment of changed fields only, so it returns <c>IsValid</c> true for every refused case and
/// <c>Assert.False(result.IsValid)</c> fails.
/// </para>
/// </summary>
public sealed class LocalProfileValidationTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    /// <summary>
    /// A profile with at most two members set (reflection sets the init-only property). A value is a string
    /// parsed by the member's type, so <c>"37.000"</c> keeps its three decimals (an attribute cannot carry a
    /// decimal) and a clock time is <c>"HH:mm:ss"</c>.
    /// </summary>
    private static LocalAgreementProfile Profile(
        string? member1, string? value1, string? member2 = null, string? value2 = null)
    {
        var profile = new LocalAgreementProfile
        {
            ProfileId = Guid.NewGuid(),
            OrgId = "STY01",
            AgreementCode = "HK",
            OkVersion = "OK24",
            EffectiveFrom = Monday,
            CreatedBy = "admin1",
            CreatedAt = DateTime.UtcNow,
            Version = 1,
        };
        Set(profile, member1, value1);
        Set(profile, member2, value2);
        return profile;
    }

    private static void Set(LocalAgreementProfile profile, string? member, string? value)
    {
        if (member is null)
            return;
        var property = typeof(LocalAgreementProfile).GetProperty(member)
            ?? throw new InvalidOperationException($"No LocalAgreementProfile member '{member}'.");
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object? parsed = type == typeof(decimal) ? decimal.Parse(value!, CultureInfo.InvariantCulture)
            : type == typeof(TimeOnly) ? TimeOnly.Parse(value!, CultureInfo.InvariantCulture)
            : value;
        property.SetValue(profile, parsed);
    }

    private static ValidationResult Validate(LocalAgreementProfile profile) =>
        new ProfileAlignmentValidator().Validate(profile, new Dictionary<string, JsonElement>());

    private static void AssertRefused(ValidationResult result, string field, string code)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == field && e.Code == code);
    }

    /// <summary>
    /// <b>F (Unit).</b> AC-C4b's pure half: a negative cap, a weekday window that ends before it starts and
    /// "credit by office hours" with no window are each refused with their field and code.
    /// </summary>
    [Theory]
    [InlineData("FlexPlusCapHours", "-1", null, null, "FlexPlusCapHours", "MUST_NOT_BE_NEGATIVE")]
    [InlineData("OfficeHoursMonStart", "16:00:00", "OfficeHoursMonEnd", "08:00:00", "OfficeHoursMonEnd", "WINDOW_START_NOT_BEFORE_END")]
    [InlineData("PaidAbsenceCreditBasis", "OFFICE_HOURS", null, null, "PaidAbsenceCreditBasis", "OFFICE_HOURS_WITHOUT_WINDOWS")]
    public void NegativeCap_InvertedWindow_OfficeHoursWithoutWindows_400(
        string member1, string value1, string? member2, string? value2, string field, string code) =>
        AssertRefused(Validate(Profile(member1, value1, member2, value2)), field, code);

    /// <summary>
    /// <b>F (Unit).</b> AC-C4f (SEC-061): the legacy numeric fields' range. Refused: <c>WeeklyNormHours</c> 0 and
    /// -5, <c>MaxFlexBalance</c> -0.01, <c>MaxOvertimeHoursPerPeriod</c> -1. Accepted: <c>MaxFlexBalance</c> 0,
    /// <c>MaxOvertimeHoursPerPeriod</c> 0 and every member null (null inherits, never refused).
    /// </summary>
    [Theory]
    [InlineData("WeeklyNormHours", "0", "WeeklyNormHours", "MUST_BE_POSITIVE")]
    [InlineData("WeeklyNormHours", "-5", "WeeklyNormHours", "MUST_BE_POSITIVE")]
    [InlineData("MaxFlexBalance", "-0.01", "MaxFlexBalance", "MUST_NOT_BE_NEGATIVE")]
    [InlineData("MaxOvertimeHoursPerPeriod", "-1", "MaxOvertimeHoursPerPeriod", "MUST_NOT_BE_NEGATIVE")]
    [InlineData("MaxFlexBalance", "0", null, null)]
    [InlineData("MaxOvertimeHoursPerPeriod", "0", null, null)]
    [InlineData(null, null, null, null)]
    public void LegacyNumericField_OutOfRange_400(string? member, string? value, string? field, string? code)
    {
        var result = Validate(Profile(member, value));
        if (field is null)
        {
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }
        else
        {
            AssertRefused(result, field, code!);
        }
    }

    /// <summary>
    /// <b>F (Unit).</b> AC-C4g (ruling 15): a value the table would reject or could not hold exactly. Refused:
    /// 10.005 (a third decimal), 10000 on <c>FlexPlusCapHours</c> and 1000 on <c>WeeklyNormHours</c> (past the
    /// column), <c>"month"</c> (not a stored option), a window start with no end (the error is on the end) and
    /// 08:00:30 (seconds). Accepted: 9999.99 (<c>NUMERIC(6,2)</c>'s own largest value) and 37.000 (the same
    /// number as 37, written with a trailing zero).
    /// </summary>
    [Theory]
    [InlineData("FlexPlusCapHours", "10.005", null, null, "FlexPlusCapHours", "TOO_MANY_DECIMALS")]
    [InlineData("FlexPlusCapHours", "10000", null, null, "FlexPlusCapHours", "EXCEEDS_STORAGE_LIMIT")]
    [InlineData("WeeklyNormHours", "1000", null, null, "WeeklyNormHours", "EXCEEDS_STORAGE_LIMIT")]
    [InlineData("FlexSettlementPeriod", "month", null, null, "FlexSettlementPeriod", "UNKNOWN_VALUE")]
    [InlineData("OfficeHoursMonStart", "08:00:00", null, null, "OfficeHoursMonEnd", "WINDOW_INCOMPLETE")]
    [InlineData("OfficeHoursMonStart", "08:00:30", "OfficeHoursMonEnd", "16:00:00", "OfficeHoursMonStart", "NOT_WHOLE_MINUTE")]
    [InlineData("FlexPlusCapHours", "9999.99", null, null, null, null)]
    [InlineData("FlexPlusCapHours", "37.000", null, null, null, null)]
    public void ValueTheTableCannotHold_400(
        string member1, string value1, string? member2, string? value2, string? field, string? code)
    {
        var result = Validate(Profile(member1, value1, member2, value2));
        if (field is null)
        {
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }
        else
        {
            AssertRefused(result, field, code!);
        }
    }
}
