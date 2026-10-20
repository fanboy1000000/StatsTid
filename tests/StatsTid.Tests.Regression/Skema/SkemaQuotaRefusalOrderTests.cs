using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S145 / Step 7a item 10 (TASK-14507, the three-phase quota pre-check in <c>SkemaEndpoints.cs</c>) — the
/// MULTI-TYPE REFUSAL ORDER. New file: the existing quota pins (<see cref="SkemaQuotaResaveTests"/>,
/// <c>SkemaAccrualCapTests</c>) each save ONE entitlement type, so none can see the order across two.
///
/// <para><b>Plain language.</b> One Skema save can book two kinds of entitlement (here ferie and særlige
/// feriedage). The pre-check looks at them one after the other, in the order they appear in the request, and
/// answers the FIRST refusal it meets. Since S145 the pre-check runs in three phases (gather per type, one
/// read-only snapshot, then decide per type). The first phase must not answer a later type's problem on the
/// spot: it only RECORDS it, because an earlier type's refusal by the rule engine comes first in the original
/// single loop, and a refusal order the owner never changed must not change as a side effect. This fact pins
/// that: ferie (first in the request) is over its quota and the rule engine refuses it; særlige feriedage
/// (second) would answer "employment profile missing". The answer must be ferie's quota refusal, and nothing
/// is written.</para>
///
/// <para><b>Expected values come from the spec</b> (ADR-018 D13 and the S145 refinement: the refusal order is
/// the single loop's: type k's profile-missing 422 answers only if no earlier type's rule-engine call refused
/// first), not from what the code returns. This fact pins EXISTING behaviour: it is green on today's code and
/// exists so a later change to the phases cannot reorder the answers unseen.</para>
///
/// <para><b>How the second type gets "profile missing".</b> A genuine missing profile on that day would be
/// refused EARLIER, by the anchor guard that runs before the pre-check (same error code), so the phase-1
/// branch is reachable only when the profile read at the phase-1 anchor disagrees with the reads before it
/// (a profile change landing mid-request, the race the in-lock re-check exists for). The fact reproduces that
/// race deterministically: the host's <see cref="IEmploymentProfileResolver"/> is wrapped, and answers "no
/// profile" for the særlige-feriedage day ONLY to the endpoint's own phase-1 read: the host wires the two calculators
/// (which serve the guards before the pre-check) to the REAL resolver, so on the save path only the endpoint gets the wrapper
/// (explicit DI, see <see cref="Harness"/>). The wrapper counts the nulls it served, and the fact asserts exactly one, so the premise cannot
/// be silently vacuous.</para>
///
/// <para><b>Fixture rules.</b> As <see cref="SkemaQuotaResaveTests"/>: VACATION exhausted by REAL saves (24
/// baseline weekdays plus the pinned Wednesday, 25.0 used) so a further full day (delta +1.0) is refused by
/// the real <c>EntitlementValidationRule</c>; every date a WEEKDAY (nudge helper, S138/QUAL-153); the setup
/// saves assert their own 200 (R62); the clock is pinned (PAT-008).</para>
///
/// <para><b>Docker-gated, CI-verified, never locally green.</b></para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaQuotaRefusalOrderTests : IClassFixture<SkemaQuotaRefusalOrderTests.Harness>
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    /// <summary>Monday 2025-03-03 (OK24 side of the 2026-04-01 cutover).</summary>
    private static readonly DateOnly F = new(2025, 3, 3);

    private static readonly DateOnly Day = NextWeekday(new DateOnly(2025, 3, 5));          // the 25th full day
    private static readonly DateOnly VacationDay = NextWeekday(new DateOnly(2025, 3, 6));  // would be the 26th
    private static readonly DateOnly SpecialHolidayDay = NextWeekday(new DateOnly(2025, 3, 7));
    private const int VacationYear = 2024; // ferieår 1 Sep 2024 - 31 Aug 2025

    private readonly Harness _fx;

    public SkemaQuotaRefusalOrderTests(Harness fx) => _fx = fx;

    /// <summary>
    /// <b>QR1.</b> A save with absences in the order [VACATION on a weekday, SPECIAL_HOLIDAY_ALLOWANCE on the
    /// next weekday], where ferie is at its quota (25.0 of 25.0 used; the new day is +1.0) and the profile
    /// read for the særlige-feriedage day fails at the pre-check: the answer is 422
    /// <c>"Entitlement quota exceeded"</c> for <c>absenceType = VACATION</c> (the earlier type's rule-engine
    /// refusal), NOT <c>employment_profile_missing</c> for SPECIAL_HOLIDAY. Nothing is written: no absence row
    /// on either day, <c>used</c> still 25.0, no new outbox event, no særlige-feriedage balance row.
    ///
    /// <para><b>Red conditions.</b> Passes on today's code. The named MUTATION is the pre-check's phase 1
    /// returning the later type's profile refusal immediately (<c>return Results.Json(... "employment_profile_missing"
    /// ...)</c> in place of recording it on the plan and <c>break</c>ing, in <c>SkemaEndpoints.cs</c> where
    /// <c>QuotaPreCheckPlan.Refused</c> is built): the save then answers 422 <c>employment_profile_missing</c>
    /// for SPECIAL_HOLIDAY before ferie's turn comes, and <c>Assert.Equal("Entitlement quota exceeded", error)</c>
    /// trips. A second mutation, phase 3 evaluating the later type's recorded refusal before the earlier type's
    /// rule call, trips the same assertion. CI-verified (Docker).</para>
    /// </summary>
    [Fact]
    public async Task Save_TwoTypes_EarlierTypeRuleRefusalAnswersBeforeLaterTypeProfileMissing_NothingWritten()
    {
        var emp = await SeedEmployeeAsync();
        await BookFullDaysAsync(emp, BaselineFullDays);
        await SaveAbsencesOkAsync(emp, Day, new[] { (Day, "VACATION", 7.4m) }, "the 25th full ferie day");
        Assert.Equal(25.0m, await UsedAsync(emp)); // premise: ferie is exhausted by real saves

        var absenceRowsBefore = await AbsenceRowCountAsync(emp);
        var outboxBefore = await OutboxCountAsync(emp);
        _fx.ProfileSeam.Arm(emp, SpecialHolidayDay);
        HttpResponseMessage rsp;
        try
        {
            rsp = await ClientFor(emp).PostAsJsonAsync($"/api/skema/{emp}/save", new
            {
                year = F.Year,
                month = F.Month,
                absences = new[]
                {
                    new { date = VacationDay.ToString("yyyy-MM-dd"), absenceType = "VACATION", hours = 7.4m },
                    new { date = SpecialHolidayDay.ToString("yyyy-MM-dd"), absenceType = "SPECIAL_HOLIDAY_ALLOWANCE", hours = 7.4m },
                },
            });
        }
        finally
        {
            _fx.ProfileSeam.Disarm();
        }

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(_fx.ProfileSeam.NullsServed == 1,
            "premise: the wrapped resolver must have served exactly one 'no profile' answer to the pre-check's " +
            $"own read (the phase-1 anchor of the særlige-feriedage type); it served {_fx.ProfileSeam.NullsServed}. Body: {raw}");

        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity, $"expected 422, got {(int)rsp.StatusCode}: {raw}");
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("Entitlement quota exceeded", body.GetProperty("error").GetString());
        Assert.Equal("VACATION", body.GetProperty("absenceType").GetString());

        Assert.Equal(absenceRowsBefore, await AbsenceRowCountAsync(emp));
        Assert.Equal(0, await AbsenceRowsOnAsync(emp, VacationDay));
        Assert.Equal(0, await AbsenceRowsOnAsync(emp, SpecialHolidayDay));
        Assert.Equal(25.0m, await UsedAsync(emp));
        Assert.Equal(outboxBefore, await OutboxCountAsync(emp));
        Assert.Equal(0, await SpecialHolidayBalanceRowCountAsync(emp));
    }

    // ── Fixture composition ──

    private static readonly IReadOnlyList<DateOnly> BaselineFullDays = WeekdaysFrom(new DateOnly(2025, 1, 2), 24);

    private async Task BookFullDaysAsync(string emp, IReadOnlyList<DateOnly> dates)
    {
        foreach (var month in dates.GroupBy(d => (d.Year, d.Month)))
            await SaveAbsencesOkAsync(emp, month.First(),
                month.Select(d => (d, "VACATION", 7.4m)).ToArray(), $"full days {month.Key.Year}-{month.Key.Month:D2}");
        Assert.Equal((decimal)dates.Count, await UsedAsync(emp));
    }

    private async Task SaveAbsencesOkAsync(
        string emp, DateOnly monthOf, (DateOnly Date, string Type, decimal Hours)[] absences, string what)
    {
        var rsp = await ClientFor(emp).PostAsJsonAsync($"/api/skema/{emp}/save", new
        {
            year = monthOf.Year,
            month = monthOf.Month,
            absences = absences.Select(a => new
            {
                date = a.Date.ToString("yyyy-MM-dd"),
                absenceType = a.Type,
                hours = a.Hours,
            }).ToArray(),
        });
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"setup save ({what}) must answer 200 before the save under test; got {(int)rsp.StatusCode}: {raw}");
    }

    private HttpClient ClientFor(string emp)
    {
        var client = _fx.Host.CreateClient();
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", svc.GenerateToken(
            employeeId: emp, name: emp, role: StatsTidRoles.Employee,
            agreementCode: "AC", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") }));
        return client;
    }

    // ── Seeding and SQL reads (literal statements only: a non-constant command text trips CA2100) ──

    private async Task<string> SeedEmployeeAsync()
    {
        var emp = "emp_s145_qro_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_fx.ConnectionString, emp, OrgId, "AC", "OK24", ensureOrg: false);
        return emp;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task<decimal> UsedAsync(string emp)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT used FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = 'VACATION' AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("y", VacationYear);
        var result = await cmd.ExecuteScalarAsync();
        Assert.False(result is null or DBNull, $"expected a VACATION/{VacationYear} balance row for {emp}.");
        return Convert.ToDecimal(result);
    }

    private async Task<long> AbsenceRowCountAsync(string emp)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM absences_projection WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", emp);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> AbsenceRowsOnAsync(string emp, DateOnly date)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM absences_projection WHERE employee_id = @e AND date = @d", conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> OutboxCountAsync(string emp)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s", conn);
        cmd.Parameters.AddWithValue("s", $"employee-{emp}");
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> SpecialHolidayBalanceRowCountAsync(string emp)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM entitlement_balances WHERE employee_id = @e AND entitlement_type = 'SPECIAL_HOLIDAY'", conn);
        cmd.Parameters.AddWithValue("e", emp);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static DateOnly NextWeekday(DateOnly d)
    {
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            d = d.AddDays(1);
        return d;
    }

    private static IReadOnlyList<DateOnly> WeekdaysFrom(DateOnly start, int count)
    {
        var dates = new List<DateOnly>(count);
        var d = NextWeekday(start);
        while (dates.Count < count)
        {
            dates.Add(d);
            d = NextWeekday(d.AddDays(1));
        }
        return dates;
    }

    // ── The profile-resolver seam: the deterministic "profile changed mid-request" ──

    /// <summary>Wraps the host's real <see cref="IEmploymentProfileResolver"/>. While armed, a read for the
    /// armed employee and day answers <c>null</c>. On the save path only the endpoint receives this wrapper (the two
    /// calculators are built on the real resolver, see <see cref="Harness"/>), so the only save-path read it can intercept is
    /// the quota pre-check's own phase-1 anchor read. Host-wide, other holders (the settlement poller, other endpoints) never
    /// read the armed day; a stray hit would make the null count 2 and fail the fact.
    /// Everything else, and every read while disarmed, is the real answer.</summary>
    public sealed class ProfileResolverSeam : IEmploymentProfileResolver
    {
        private readonly IEmploymentProfileResolver _inner;
        private volatile string? _employeeId;
        private DateOnly _day;
        private int _nullsServed;

        public ProfileResolverSeam(IEmploymentProfileResolver inner) => _inner = inner;

        public int NullsServed => Volatile.Read(ref _nullsServed);

        public void Arm(string employeeId, DateOnly day)
        {
            Interlocked.Exchange(ref _nullsServed, 0);
            _day = day;
            _employeeId = employeeId;
        }

        public void Disarm() => _employeeId = null;

        public Task<EmploymentProfile?> GetByEmployeeIdAtAsync(
            string employeeId, DateOnly asOfDate, CancellationToken ct = default)
        {
            if (string.Equals(_employeeId, employeeId, StringComparison.Ordinal)
                && asOfDate == _day)
            {
                Interlocked.Increment(ref _nullsServed);
                return Task.FromResult<EmploymentProfile?>(null);
            }
            return _inner.GetByEmployeeIdAtAsync(employeeId, asOfDate, ct);
        }

    }

    // ── The shared container + host ──

    public sealed class Harness : IAsyncLifetime
    {
        private TestFixtures.DockerHarness _harness = null!;
        private StatsTidWebApplicationFactory _factory = null!;

        public WebApplicationFactory<Program> Host { get; private set; } = null!;
        public string ConnectionString { get; private set; } = null!;

        /// <summary>The wrapper instance the host actually uses (the singleton is built on first resolve).</summary>
        public ProfileResolverSeam ProfileSeam =>
            (ProfileResolverSeam)Host.Services.GetRequiredService<IEmploymentProfileResolver>();

        public async Task InitializeAsync()
        {
            _harness = await TestFixtures.DockerHarness.StartAsync();
            ConnectionString = _harness.ConnectionString;
            await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(ConnectionString);
            _factory = new StatsTidWebApplicationFactory(ConnectionString);
            Host = _factory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory());
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(F));
                    // Explicit wiring (R-133): the real resolver is registered as ITSELF; the two calculators
                    // are built on it directly (the save endpoint injects ConsumptionCalculator, which carries DailyNormCalculator); and only
                    // IEmploymentProfileResolver is the armed wrapper, which the endpoint's own phase-1 read
                    // is the sole consumer of in the save path.
                    services.AddSingleton<EmploymentProfileResolver>(sp =>
                        ActivatorUtilities.CreateInstance<EmploymentProfileResolver>(sp));
                    services.AddSingleton<StatsTid.Backend.Api.Services.DailyNormCalculator>(sp =>
                        new StatsTid.Backend.Api.Services.DailyNormCalculator(
                            sp.GetRequiredService<EmploymentProfileResolver>(),
                            sp.GetRequiredService<ConfigResolutionService>()));
                    services.AddSingleton<StatsTid.Backend.Api.Services.ConsumptionCalculator>(sp =>
                        new StatsTid.Backend.Api.Services.ConsumptionCalculator(
                            sp.GetRequiredService<StatsTid.Backend.Api.Services.DailyNormCalculator>(),
                            sp.GetRequiredService<EmploymentProfileResolver>()));
                    services.AddSingleton<IEmploymentProfileResolver>(sp =>
                        new ProfileResolverSeam(sp.GetRequiredService<EmploymentProfileResolver>()));
                }));
            _ = Host.CreateClient(); // boot the startup seeders now; every case seeds after this
        }

        public async Task DisposeAsync()
        {
            Host?.Dispose();
            _factory?.Dispose();
            if (_harness is not null)
                await _harness.DisposeAsync();
        }
    }

    private sealed class RuleEngineStubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new EntitlementValidationStubHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("http://rule-engine:8080"),
        };
    }
}
