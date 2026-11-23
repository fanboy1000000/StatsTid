using Microsoft.AspNetCore.Http;
using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S148 T14b FU-4T (5a ruling U1): a refusal on a no-record weekday d is remembered for the request on every day on or
/// before the bound, with no lower bound, so a later person's earlier days never ask the refused lookup again.
///
/// <para>
/// <b>Plain language.</b> Teamoversigt computes a month for each person on one request. If agreement X has nothing in
/// force before Monday 16 March 2026, the first person with a gap on 11-13 March makes the lookup refuse (and log an
/// Error line) on those days. A second person whose gap starts earlier (2-10 March) must not make it refuse again: nothing
/// in force on 11 March means nothing in force on 2 March. March 2026 has 22 weekdays; at 37 h a day is 7.40, so
/// 22 x 7.40 = <b>162.80</b>. Hand-derived from the ruling, not from the code.
/// </para>
///
/// <para>
/// <b>Seam.</b> The real calculator built without the date repositories but with an <see cref="IHttpContextAccessor"/>
/// holding one context, so two calls share one request's memo. The fake lookup answers by agreement code: X before
/// 16 March refuses and records the day; anything else gives 37 h. Refusals are counted, not total lookups.
/// </para>
///
/// <para>
/// <b>Red conditions.</b> F fact: at <c>e1787c56</c> the memo covered a refusal on d only from d onward, so call B asks
/// the refused lookup on 2, 3, 4, 5, 6, 9 and 10 March (7 refusals, expected 0). R fact: green before and after; a
/// record-covered day never consults the memo, so it still refuses by name after the memo is written.
/// </para>
/// </summary>
public sealed class ComputeMonthNormMemoBoundTests
{
    private static readonly DateOnly Mar01 = new(2026, 3, 1);  // Sunday
    private static readonly DateOnly Mar10 = new(2026, 3, 10); // Tuesday
    private static readonly DateOnly Mar16 = new(2026, 3, 16); // Monday
    private static readonly DateOnly Mar31 = new(2026, 3, 31); // Tuesday

    private static DatedRecord? CallA(DateOnly d) =>
        d <= Mar10 ? new DatedRecord("Y", null, "ORG") : d >= Mar16 ? new DatedRecord("X", null, "ORG") : null;

    private static DatedRecord? CallB(DateOnly d) => d >= Mar16 ? new DatedRecord("X", null, "ORG") : null;

    private static (DailyNormCalculator Calculator, CountingLookup Lookup) Build()
    {
        var lookup = new CountingLookup();
        var calculator = new DailyNormCalculator(
            new NoProfileResolver(), lookup,
            httpContextAccessor: new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        return (calculator, lookup);
    }

    /// <summary>F. Call A refuses on 11-13 March; call B on the same request adds no refusal and still gives 162.80.</summary>
    [Fact]
    public async Task LaterPersonsEarlierNoRecordDays_AfterARefusal_AddNoRefusedLookup()
    {
        var (calculator, lookup) = Build();

        var normA = await calculator.ComputeMonthNormAsync(CallA, Mar01, Mar31, 1.0m, "X");
        var refusedByA = lookup.Refused.Count;
        var normB = await calculator.ComputeMonthNormAsync(CallB, Mar01, Mar31, 1.0m, "X");

        Assert.Equal(162.80m, normA);
        Assert.Equal(3, refusedByA); // 11, 12 and 13 March
        Assert.Equal(0, lookup.Refused.Count - refusedByA);
        Assert.Equal(162.80m, normB);
    }

    /// <summary>R. After the memo is written, a record-covered day before 16 March on X still refuses by name.</summary>
    [Fact]
    public async Task AfterMemoWritten_RecordCoveredDayBefore16March_StillThrowsNoConfigInForce()
    {
        var (calculator, _) = Build();
        await calculator.ComputeMonthNormAsync(CallA, Mar01, Mar31, 1.0m, "X");
        await calculator.ComputeMonthNormAsync(CallB, Mar01, Mar31, 1.0m, "X");

        var thrown = await Assert.ThrowsAsync<NoConfigInForceException>(() => calculator.ComputeMonthNormAsync(
            _ => new DatedRecord("X", null, "ORG"), Mar01, Mar31, 1.0m, "X"));

        Assert.Equal("X", thrown.AgreementCode);
        Assert.Equal(new DateOnly(2026, 3, 2), thrown.Day);
    }

    private sealed class CountingLookup : IAgreementConfigLookup
    {
        public List<DateOnly> Refused { get; } = new();

        public Task<AgreementRuleConfig> ResolveAsync(
            string orgId, string agreementCode, string okVersion, string? position, DateOnly asOf,
            CancellationToken ct = default) => Task.FromResult(Answer(agreementCode, okVersion, asOf));

        public Task<AgreementRuleConfig> ResolveCentralAsync(
            string agreementCode, string okVersion, DateOnly asOf, CancellationToken ct = default) =>
            Task.FromResult(Answer(agreementCode, okVersion, asOf));

        private AgreementRuleConfig Answer(string agreementCode, string okVersion, DateOnly asOf)
        {
            if (agreementCode == "X" && asOf < Mar16)
            {
                Refused.Add(asOf);
                throw new NoConfigInForceException(agreementCode, okVersion, asOf);
            }

            return new AgreementRuleConfig
            {
                AgreementCode = agreementCode,
                OkVersion = okVersion,
                WeeklyNormHours = 37m,
                HasOvertime = false,
                HasMerarbejde = false,
                MaxFlexBalance = 0m,
                EveningSupplementEnabled = false,
                NightSupplementEnabled = false,
                WeekendSupplementEnabled = false,
                HolidaySupplementEnabled = false,
            };
        }
    }

    private sealed class NoProfileResolver : IEmploymentProfileResolver
    {
        public Task<EmploymentProfile?> GetByEmployeeIdAtAsync(string employeeId, DateOnly asOfDate, CancellationToken ct = default) =>
            Task.FromResult<EmploymentProfile?>(null);
    }
}
