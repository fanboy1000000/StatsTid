using System.Text.Json;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14811-T (QUAL-236), AC-S0-2b — the child's sick-day codes the month view offers. In plain terms: the
/// catalogue of absence types an employee sees depends on the OK version of the month: from 1 April 2026 (OK26) an
/// illness gives three days plus the call-home day, before that two days and no call-home day, whatever the agreement.
/// The catalogue is decided once per month at the month's last day (the dated CHILD_SICK config).
///
/// <para>Expected values come from the agreement texts (OK24: 2 days, CIR1H 9333 § 18 stk. 6, 029-24 § 14; OK26: 3 days
/// plus the call-home day, 021-26 § 14), never from the code. Both catalogue surfaces are asserted (the legacy
/// <c>absenceTypes</c> and <c>catalogs.absenceTypes</c>, one computation and two projections; the precedent is
/// <c>SkemaSpecialHolidayRowTests</c>). The employee is opted in: the child codes are served only to an opted-in
/// employee. Docker-gated, <b>CI-pending</b> (CI-W2); never run locally.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ChildSickCatalogueTests : IAsyncLifetime
{
    private ChildSickHost _host = null!;

    public async Task InitializeAsync() => _host = await ChildSickHost.StartAsync();

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    /// <summary>
    /// AC-S0-2b. An opted-in HK employee's month view of April 2026 (anchor 30 April, OK26) serves exactly the child's
    /// codes <c>CHILD_SICK_DAY</c>, <c>CHILD_SICK_DAY_2</c>, <c>CHILD_SICK_DAY_3</c> and <c>CHILD_SICK_CALL_HOME</c>, once
    /// each, on both catalogue surfaces.
    ///
    /// <para><b>Kind F.</b> Red conditions: the set assertion. The base's HK catalogue has day 1 and day 2 only and no
    /// call-home code (<c>SkemaEndpoints.cs:50-64</c> at <c>6ec6a111</c>).</para>
    /// </summary>
    [Fact]
    public async Task April2026Hk_OffersDayTwoThreeAndCallHome()
    {
        var emp = await _host.SeedEmployeeAsync("HK");

        var body = await _host.GetMonthAsync(emp, "HK", 2026, 4);

        AssertChildCodes(body, new[] { "CHILD_SICK_CALL_HOME", "CHILD_SICK_DAY", "CHILD_SICK_DAY_2", "CHILD_SICK_DAY_3" });
    }

    /// <summary>
    /// AC-S0-2b. An opted-in PROSA employee's month view of March 2026 (anchor 31 March, OK24) serves exactly
    /// <c>CHILD_SICK_DAY</c> and <c>CHILD_SICK_DAY_2</c>: not the third-day code and not the call-home code.
    ///
    /// <para><b>Kind F.</b> Red conditions: the set assertion. The base's PROSA catalogue always serves day 3
    /// (<c>SkemaEndpoints.cs:57-61</c> at <c>6ec6a111</c>), whatever the OK version.</para>
    /// </summary>
    [Fact]
    public async Task March2026Prosa_OffersDayAndTwoOnly()
    {
        var emp = await _host.SeedEmployeeAsync("PROSA", "OK24");

        var body = await _host.GetMonthAsync(emp, "PROSA", 2026, 3);

        AssertChildCodes(body, new[] { "CHILD_SICK_DAY", "CHILD_SICK_DAY_2" });
    }

    private static void AssertChildCodes(JsonElement body, string[] expected)
    {
        foreach (var (surface, types) in new[]
        {
            ("absenceTypes", body.GetProperty("absenceTypes")),
            ("catalogs.absenceTypes", body.GetProperty("catalogs").GetProperty("absenceTypes")),
        })
        {
            var served = types.EnumerateArray()
                .Select(e => e.GetProperty("type").GetString()!)
                .Where(t => t.StartsWith("CHILD_SICK", StringComparison.Ordinal))
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToArray();
            Assert.True(
                expected.SequenceEqual(served),
                $"{surface}: expected the child codes [{string.Join(", ", expected)}], served [{string.Join(", ", served)}]");
        }
    }
}
