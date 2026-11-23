using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S148 T14b FU-T (5a ruling T2): the month's expected hours (Teamoversigt, the balance summary) on a weekday that no
/// dated employment record covers and whose agreement has no configuration in force that day.
///
/// <para>
/// <b>Plain language.</b> A new agreement is published "from today" by default. A person hired onto it on Monday
/// 16 March 2026 has no record before that day, and the agreement has nothing in force before it either. Those
/// earlier weekdays are a stand-in on the screen anyway (they read the month-end record, QUAL-226), so they must
/// never take the screen down: each such day reads the same agreement's configuration at month end, and counts
/// 0.00 h when none is in force at month end either. March 2026 has 22 weekdays (2-6, 9-13, 16-20, 23-27, 30-31);
/// at 37 h a day is 37 x 1 / 5 = 7.40, so the month is 22 x 7.40 = <b>162.80</b>. February 2026 has 20 weekdays,
/// all counted 0.00, so <b>0.00</b>. Hand-derived from the ruling, not from the code.
/// </para>
///
/// <para>
/// <b>Seam.</b> The real calculator built without the three date repositories (its documented test construction)
/// and a fake <see cref="IAgreementConfigLookup"/>. Without the repositories the fake sees one refusal per
/// refused day, by design, so no call count is asserted.
/// </para>
///
/// <para>
/// <b>Red conditions.</b> F facts (<see cref="March2026_RecordFromMonday16March_Is162Point80"/> and
/// <see cref="February2026_NoRecordNothingInForce_IsZero"/>): at <c>043c5f35</c> the first no-record weekday
/// (Monday 2 March, Monday 2 February) throws <see cref="NoConfigInForceException"/> out of the method, so both are
/// red. R facts (a record-covered day still refuses; an unavailable lookup still propagates): green before and
/// after, they pin the two things the ruling leaves fail-closed.
/// </para>
/// </summary>
public sealed class ComputeMonthNormNoRecordDayTests
{
    private static readonly DateOnly Mar16 = new(2026, 3, 16); // Monday
    private static readonly DateOnly Mar01 = new(2026, 3, 1);  // Sunday
    private static readonly DateOnly Mar31 = new(2026, 3, 31); // Tuesday
    private static readonly DateOnly Feb01 = new(2026, 2, 1);  // Sunday
    private static readonly DateOnly Feb28 = new(2026, 2, 28); // Saturday

    /// <summary>F. Agreement X (37 h) in force from Monday 16 March; the record runs from 16 March: 22 x 7.40.</summary>
    [Fact]
    public async Task March2026_RecordFromMonday16March_Is162Point80()
    {
        var lookup = new FakeLookup(asOf => asOf >= Mar16 ? Config(37m) : throw Refusal(asOf));
        var calculator = new DailyNormCalculator(new NoProfileResolver(), lookup);

        var norm = await calculator.ComputeMonthNormAsync(
            d => d >= Mar16 ? new DatedRecord("X", null, "ORG") : null,
            Mar01, Mar31, 1.0m, "X");

        Assert.Equal(162.80m, norm);
    }

    /// <summary>F. No record at all and nothing in force through February: every weekday counts 0.00 h.</summary>
    [Fact]
    public async Task February2026_NoRecordNothingInForce_IsZero()
    {
        var lookup = new FakeLookup(asOf => throw Refusal(asOf));
        var calculator = new DailyNormCalculator(new NoProfileResolver(), lookup);

        var norm = await calculator.ComputeMonthNormAsync(_ => null, Feb01, Feb28, 1.0m, "X");

        Assert.Equal(0.00m, norm);
    }

    /// <summary>R. A day a record covers still refuses by name when nothing is in force that day.</summary>
    [Fact]
    public async Task RecordCoveredDay_NothingInForce_StillThrowsNoConfigInForce()
    {
        var lookup = new FakeLookup(asOf => asOf >= Mar16 ? Config(37m) : throw Refusal(asOf));
        var calculator = new DailyNormCalculator(new NoProfileResolver(), lookup);

        var thrown = await Assert.ThrowsAsync<NoConfigInForceException>(() => calculator.ComputeMonthNormAsync(
            _ => new DatedRecord("X", null, "ORG"),
            Mar01, Mar31, 1.0m, "X"));

        Assert.Equal("X", thrown.AgreementCode);
        Assert.True(thrown.Day < Mar16);
    }

    /// <summary>R. An unavailable lookup is never absorbed as "nothing in force", even on a no-record day.</summary>
    [Fact]
    public async Task NoRecordDay_LookupUnavailable_Propagates()
    {
        var lookup = new FakeLookup(asOf => asOf >= Mar16
            ? Config(37m)
            : throw new ConfigLookupUnavailableException("central agreement configuration", new InvalidOperationException("down")));
        var calculator = new DailyNormCalculator(new NoProfileResolver(), lookup);

        var thrown = await Assert.ThrowsAsync<ConfigLookupUnavailableException>(() => calculator.ComputeMonthNormAsync(
            d => d >= Mar16 ? new DatedRecord("X", null, "ORG") : null,
            Mar01, Mar31, 1.0m, "X"));

        Assert.Equal("central agreement configuration", thrown.Layer);
    }

    private static NoConfigInForceException Refusal(DateOnly asOf) => new("X", "OK24", asOf);

    private static AgreementRuleConfig Config(decimal weeklyNormHours) => new()
    {
        AgreementCode = "X",
        OkVersion = "OK24",
        WeeklyNormHours = weeklyNormHours,
        HasOvertime = false,
        HasMerarbejde = false,
        MaxFlexBalance = 0m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
    };

    /// <summary>Answers per day through <paramref name="answer"/>, which returns a configuration or throws.</summary>
    private sealed class FakeLookup(Func<DateOnly, AgreementRuleConfig> answer) : IAgreementConfigLookup
    {
        public Task<AgreementRuleConfig> ResolveAsync(
            string orgId, string agreementCode, string okVersion, string? position, DateOnly asOf,
            CancellationToken ct = default) => Task.FromResult(answer(asOf));

        public Task<AgreementRuleConfig> ResolveCentralAsync(
            string agreementCode, string okVersion, DateOnly asOf, CancellationToken ct = default) =>
            Task.FromResult(answer(asOf));
    }

    /// <summary>Not used by <c>ComputeMonthNormAsync</c> (the records are handed in).</summary>
    private sealed class NoProfileResolver : IEmploymentProfileResolver
    {
        public Task<EmploymentProfile?> GetByEmployeeIdAtAsync(string employeeId, DateOnly asOfDate, CancellationToken ct = default) =>
            Task.FromResult<EmploymentProfile?>(null);
    }
}
