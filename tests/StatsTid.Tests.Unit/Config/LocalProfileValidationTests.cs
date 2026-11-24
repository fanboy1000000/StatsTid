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
/// Every profile in the theories is dated Monday 2 March 2026 and the changed-fields map is empty, so only the
/// value rules can refuse (the suppression fact is the exception: Wednesday 4 March with a non-empty map).
/// <b>Kind F (Unit), every theory and the fact.</b> Red conditions: before TASK-14832 <c>Validate</c> checks the
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
        string? member1, string? value1, string? member2 = null, string? value2 = null, DateOnly? effectiveFrom = null)
    {
        var profile = new LocalAgreementProfile
        {
            ProfileId = Guid.NewGuid(),
            OrgId = "STY01",
            AgreementCode = "HK",
            OkVersion = "OK24",
            EffectiveFrom = effectiveFrom ?? Monday,
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

    /// <summary>The complete error list, in order: field, code and a null <c>nearestValid</c> for each (a value
    /// error carries no dates).</summary>
    private static void AssertErrors(ValidationResult result, params (string Field, string Code)[] expected)
    {
        Assert.Equal(expected.Length == 0, result.IsValid);
        Assert.Equal(expected, result.Errors.Select(e => (e.Field, e.Code)).ToArray());
        Assert.All(result.Errors, e => Assert.Null(e.NearestValid));
    }

    private static void AssertRefused(ValidationResult result, string field, string code) =>
        AssertErrors(result, (field, code));

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

    // ---- The table: every member, exact error lists (R1; SEC-061) ----

    private static readonly string[] Days = { "Mon", "Tue", "Wed", "Thu", "Fri" };

    /// <summary>
    /// Each row: a label, the members to set (<c>"Member=value;Member=value"</c>, <c>null</c> as the value clears
    /// the member, later entries override earlier ones) and the expected complete error list
    /// (<c>"Field:CODE;Field:CODE"</c>, empty for accepted). Limits and codes are literals from ruling 4.
    /// </summary>
    public static IEnumerable<object[]> ValueRuleRows()
    {
        // Numeric members: (member, sign code, column limit, one over the limit).
        var numerics = new (string Member, string SignCode, string Limit, string OverLimit)[]
        {
            ("WeeklyNormHours", "MUST_BE_POSITIVE", "999.99", "1000.00"),
            ("MaxFlexBalance", "MUST_NOT_BE_NEGATIVE", "9999.99", "10000.00"),
            ("MaxOvertimeHoursPerPeriod", "MUST_NOT_BE_NEGATIVE", "9999.99", "10000.00"),
            ("FlexPlusCapHours", "MUST_NOT_BE_NEGATIVE", "9999.99", "10000.00"),
            ("FlexMinusCapHours", "MUST_NOT_BE_NEGATIVE", "9999.99", "10000.00"),
            ("FlexDailyCreditCapHours", "MUST_NOT_BE_NEGATIVE", "999.99", "1000.00"),
        };
        foreach (var (m, sign, limit, over) in numerics)
        {
            yield return Row($"{m} negative", $"{m}=-1", $"{m}:{sign}");
            yield return Row($"{m} third decimal", $"{m}=10.005", $"{m}:TOO_MANY_DECIMALS");
            yield return Row($"{m} over its limit", $"{m}={over}", $"{m}:EXCEEDS_STORAGE_LIMIT");
            yield return Row($"{m} at its limit", $"{m}={limit}", "");
        }
        yield return Row("WeeklyNormHours zero", "WeeklyNormHours=0", "WeeklyNormHours:MUST_BE_POSITIVE");

        // The five vocabulary members.
        foreach (var m in new[] { "FlexSettlementPeriod", "FlexCapTestPeriod", "FlexAboveCapAction", "FlexLeavingAction", "PaidAbsenceCreditBasis" })
        {
            yield return Row($"{m} x", $"{m}=x", $"{m}:UNKNOWN_VALUE");
            yield return Row($"{m} empty", $"{m}=", $"{m}:UNKNOWN_VALUE");
        }

        // Each weekday, against a background of the other four days holding a complete window: a window
        // wired to the wrong day (Tuesday's end read from Monday) is then visible.
        foreach (var d in Days)
        {
            var background = string.Join(";", Days.Where(o => o != d).Select(o => $"OfficeHours{o}Start=09:00:00;OfficeHours{o}End=15:00:00"));
            string S(string start, string end) => $"{background};OfficeHours{d}Start={start};OfficeHours{d}End={end}";
            yield return Row($"{d} window 08:00-16:00", S("08:00:00", "16:00:00"), "");
            yield return Row($"{d} lone start", S("08:00:00", "null"), $"OfficeHours{d}End:WINDOW_INCOMPLETE");
            yield return Row($"{d} lone end", S("null", "16:00:00"), $"OfficeHours{d}Start:WINDOW_INCOMPLETE");
            yield return Row($"{d} equal times", S("08:00:00", "08:00:00"), $"OfficeHours{d}End:WINDOW_START_NOT_BEFORE_END");
            yield return Row($"{d} inverted times", S("16:00:00", "08:00:00"), $"OfficeHours{d}End:WINDOW_START_NOT_BEFORE_END");
            yield return Row($"{d} seconds on the start", S("08:00:30", "16:00:00"), $"OfficeHours{d}Start:NOT_WHOLE_MINUTE");
            yield return Row($"{d} seconds on the end", S("08:00:00", "16:00:30"), $"OfficeHours{d}End:NOT_WHOLE_MINUTE");
        }

        // The credit basis against the windows.
        yield return Row("OFFICE_HOURS no window", "PaidAbsenceCreditBasis=OFFICE_HOURS", "PaidAbsenceCreditBasis:OFFICE_HOURS_WITHOUT_WINDOWS");
        yield return Row("OFFICE_HOURS one complete window",
            "PaidAbsenceCreditBasis=OFFICE_HOURS;OfficeHoursWedStart=08:00:00;OfficeHoursWedEnd=16:00:00", "");
        yield return Row("OFFICE_HOURS with only a lone start",
            "PaidAbsenceCreditBasis=OFFICE_HOURS;OfficeHoursMonStart=08:00:00",
            "OfficeHoursMonEnd:WINDOW_INCOMPLETE;PaidAbsenceCreditBasis:OFFICE_HOURS_WITHOUT_WINDOWS");

        // Overlapping failures on one field: one error per field, sign before decimals before limit.
        yield return Row("negative plus third decimal: the sign only", "FlexPlusCapHours=-10.001", "FlexPlusCapHours:MUST_NOT_BE_NEGATIVE");
        yield return Row("weekly norm negative plus third decimal: the sign only", "WeeklyNormHours=-0.001", "WeeklyNormHours:MUST_BE_POSITIVE");
        yield return Row("third decimal plus over the limit: the decimals only", "FlexPlusCapHours=10000.001", "FlexPlusCapHours:TOO_MANY_DECIMALS");
        yield return Row("weekly norm third decimal plus over the limit: the decimals only", "WeeklyNormHours=1000.001", "WeeklyNormHours:TOO_MANY_DECIMALS");

        // Seconds on a time inside an incomplete window: its own NOT_WHOLE_MINUTE, then the missing member's
        // WINDOW_INCOMPLETE (a different field), and never an order error.
        yield return Row("seconds on a lone start", "OfficeHoursMonStart=08:00:30",
            "OfficeHoursMonStart:NOT_WHOLE_MINUTE;OfficeHoursMonEnd:WINDOW_INCOMPLETE");
        yield return Row("seconds on a lone end", "OfficeHoursTueEnd=16:00:30",
            "OfficeHoursTueEnd:NOT_WHOLE_MINUTE;OfficeHoursTueStart:WINDOW_INCOMPLETE");
        yield return Row("seconds on both times of an inverted window", "OfficeHoursFriStart=16:00:30;OfficeHoursFriEnd=08:00:30",
            "OfficeHoursFriStart:NOT_WHOLE_MINUTE;OfficeHoursFriEnd:NOT_WHOLE_MINUTE");

        // Several fields: the members' order in the validator.
        yield return Row("several fields in member order", "FlexPlusCapHours=-1;WeeklyNormHours=0;FlexSettlementPeriod=x",
            "WeeklyNormHours:MUST_BE_POSITIVE;FlexSettlementPeriod:UNKNOWN_VALUE;FlexPlusCapHours:MUST_NOT_BE_NEGATIVE");

        static object[] Row(string label, string members, string expected) => new object[] { label, members, expected };
    }

    /// <summary>
    /// <b>F (Unit).</b> R1 (SEC-061; ADR-043 D2, D3, D6): every numeric member (negative, a third decimal, one
    /// over its own column limit, the limit itself accepted), <c>WeeklyNormHours</c> 0, every vocabulary member
    /// with <c>"x"</c> and <c>""</c>, every weekday's window rules and the credit basis, each asserted as the
    /// <b>complete</b> error list, plus the overlapping failures that fix the one-error-per-field precedence.
    /// <para><b>Red conditions (F).</b> Mutations that turn it red: <c>FlexDailyCreditCapHours</c> checked against
    /// the 6,2 limit (its over-limit row is accepted); the vocabulary check using <c>string.IsNullOrEmpty</c> (the
    /// empty rows are accepted); Tuesday's window reading Monday's end (the Tuesday lone-start row is accepted);
    /// a first-failure rule that stops stopping (the overlap rows list a second error).</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ValueRuleRows))]
    public void ValueRules_ExactErrorList(string label, string members, string expected)
    {
        Assert.NotEmpty(label);
        var profile = Profile(null, null);
        foreach (var assignment in members.Split(';'))
        {
            var cut = assignment.IndexOf('=');
            var value = assignment[(cut + 1)..];
            SetOrClear(profile, assignment[..cut], value == "null" ? null : value);
        }

        var expectedErrors = expected.Length == 0
            ? Array.Empty<(string, string)>()
            : expected.Split(';').Select(e => (e[..e.IndexOf(':')], e[(e.IndexOf(':') + 1)..])).ToArray();
        AssertErrors(Validate(profile), expectedErrors);
    }

    private static void SetOrClear(LocalAgreementProfile profile, string member, string? value)
    {
        if (value is not null)
        {
            Set(profile, member, value);
            return;
        }
        typeof(LocalAgreementProfile).GetProperty(member)!.SetValue(profile, null);
    }

    /// <summary>
    /// <b>F (Unit).</b> The alignment-suppression rule (ADR-017 D9a with ruling 4): a profile dated Wednesday 4
    /// March 2026 whose <c>WeeklyNormHours</c> is also in a non-empty changed-fields map answers the value code
    /// only, never also <c>NOT_MONDAY_ALIGNED</c> (the server's contract is at most one error per field, ruling 4;
    /// the editor shows the first it receives). A valid value on the same date is still refused for the date, which keeps the rule narrow.
    /// <para><b>Red conditions (F).</b> Without the suppression the first case lists two errors for
    /// <c>WeeklyNormHours</c>.</para>
    /// </summary>
    [Fact]
    public void ValueError_OnAChangedField_OffAMonday_SuppressesTheAlignmentError()
    {
        var wednesday = new DateOnly(2026, 3, 4);
        var changed = new Dictionary<string, JsonElement>
        {
            ["WeeklyNormHours"] = JsonDocument.Parse("-5").RootElement,
        };

        var bad = Profile("WeeklyNormHours", "-5", effectiveFrom: wednesday);
        AssertErrors(new ProfileAlignmentValidator().Validate(bad, changed), ("WeeklyNormHours", "MUST_BE_POSITIVE"));

        var valid = Profile("WeeklyNormHours", "37", effectiveFrom: wednesday);
        var result = new ProfileAlignmentValidator().Validate(valid, changed);
        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(("WeeklyNormHours", "NOT_MONDAY_ALIGNED"), (error.Field, error.Code));
        Assert.Equal(new[] { new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 9) }, error.NearestValid);
    }
}
