using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// <b>S148 / TASK-14842-T (QUAL-196; owner Q1, 2026-11-11) — a balance record created late starts at
/// what was already booked.</b> AC-BS-1, AC-BS-2 and AC-BS-5 of addendum A1 § 4.
///
/// <para><b>Plain language.</b> Every employee has one balance record per holiday type and holiday year
/// (<c>entitlement_balances</c>: how many days are <c>used</c>, out of which <c>total_quota</c>). When that
/// record is missing at the moment a day is booked or reduced, the Skema save creates it. Before this
/// change the new record started at 0 used, as if nothing had ever been booked, although the employee's
/// current registrations were already in <c>absences_projection</c>: Mette had two days booked, booked a
/// third, and the record said 1. Now the new record starts at the sum of the recorded <c>feriedage</c> of
/// the type's current registrations in that holiday year, and the save's own change is then counted on top
/// (an increase) or already inside it (a reduction). A record that already exists is never re-counted
/// (AC-BS-5).</para>
///
/// <para><b>Expected values come from the spec</b> (addendum A1 § 4, ADR-032 D2: every used value is a sum
/// of recorded <c>feriedage</c>, never a recomputation), not from what the code returns. The window of the
/// sum is the type's own year (<c>EntitlementPeriodResolver</c>): VACATION (reset month 9) 1 Sep Y – 31 Aug
/// Y+1; SPECIAL_HOLIDAY the taking window 1 May Y+1 – 30 Apr Y+2 for accrual year Y.</para>
///
/// <para><b>The shared fixture</b> (AC-BS-1, -2, -5): an HK full-time employee; <c>VACATION</c> 7,4 h saved
/// through the real save on Wed 10 and Thu 11 June 2026 (ferieår 2025), Wed 10 saved twice so it also holds
/// a retired row; then the (VACATION, 2025) balance row is deleted by SQL. SQL-seeded, with
/// <c>source = 'SKEMA'</c> and a stored <c>feriedage</c> of 1,0: a <c>VACATION</c> day in the previous ferieår
/// (Fri 29 Aug 2025, year 2024); a <c>VACATION</c> day in the NEXT ferieår (Thu 1 Oct 2026, year 2026; it
/// catches a VACATION window run on to the 31 Dec boundary); a <c>SPECIAL_HOLIDAY_ALLOWANCE</c> on Mon 8 June
/// 2026 (SPECIAL_HOLIDAY year 2025); and a <c>SPECIAL_HOLIDAY_ALLOWANCE</c> on Thu 30 Apr 2026 (a
/// January–April date keys two years back: year 2024; it catches a SPECIAL_HOLIDAY window that starts too
/// early). No (SPECIAL_HOLIDAY, 2025) row exists. Not counted toward the VACATION seed: the retired row,
/// Fri 29 Aug 2025, Thu 1 Oct 2026 and both SPECIAL_HOLIDAY rows. Not counted toward the SPECIAL_HOLIDAY
/// seed: Thu 30 Apr 2026 and every VACATION row. Every dated absence is a weekday (asserted in
/// <see cref="SeedFixtureAsync"/>; a zero-norm day is rejected by the product and skipped by revaluation,
/// S138 / QUAL-153).</para>
///
/// <para><b>Docker-gated; CI-pending, never locally green.</b> Docker does not run on the owner's machine.
/// Each fact names its kind and its "Red conditions" on the base <c>87b26453</c>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EntitlementBalanceSeedTests : IClassFixture<EntitlementBalanceSeedTests.Harness>
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string Vacation = "VACATION";
    private const string SpecialHoliday = "SPECIAL_HOLIDAY";
    private const string SpecialHolidayAllowance = "SPECIAL_HOLIDAY_ALLOWANCE";
    private const int EntitlementYear = 2025;

    /// <summary>The pinned "today" (PAT-008): Mon 2026-06-15, after every date the facts book and on the
    /// OK26 side of the 2026-04-01 cutover.</summary>
    private static readonly DateOnly F = new(2026, 6, 15);

    private static readonly DateOnly Wed10 = new(2026, 6, 10);
    private static readonly DateOnly Thu11 = new(2026, 6, 11);
    private static readonly DateOnly Fri12 = new(2026, 6, 12);
    private static readonly DateOnly Tue9 = new(2026, 6, 9);
    private static readonly DateOnly Mon8 = new(2026, 6, 8);
    private static readonly DateOnly PreviousFerieaarDay = new(2025, 8, 29);   // Fri; VACATION year 2024
    private static readonly DateOnly NextFerieaarDay = new(2026, 10, 1);       // Thu; VACATION year 2026
    private static readonly DateOnly EarlySpecialHolidayDay = new(2026, 4, 30); // Thu; SPECIAL_HOLIDAY year 2024

    private readonly Harness _fx;

    public EntitlementBalanceSeedTests(Harness fx) => _fx = fx;

    // ════════════════════════════════════════════════════════════════════════
    // AC-BS-1 — a booking increase on a missing row (CheckAndAdjustInternalAsync)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>AC-BS-1 — kind F (CI).</b> A booking that creates the missing balance row starts it at the Σ of
    /// the type's current rows in the type's own window, with the save's delta on top.
    /// <para><b>(VACATION)</b> Saving <c>VACATION</c> 7,4 on Fri 12 June: 200; the (VACATION, 2025) row
    /// exists with <c>used</c> 3,0 (the three current 2025 rows: Wed 10, Thu 11 and the new Fri 12), not 1,0
    /// (the old seed, delta only) and not 4,0 (Thu 1 Oct 2026 keys to ferieår 2026 and must not count); the
    /// <c>EntitlementBalanceAdjusted</c> event's <c>deltaDays</c> is 1,0 and <c>newUsed</c> 3,0.</para>
    /// <para><b>(SPECIAL_HOLIDAY)</b> Saving <c>SPECIAL_HOLIDAY_ALLOWANCE</c> 7,4 on Tue 9 June 2026: 200; the
    /// (SPECIAL_HOLIDAY, 2025) row exists with <c>used</c> 2,0 (Mon 8 June's 1,0 and the new day), not 1,0
    /// and not 3,0 (Thu 30 Apr 2026 keys to year 2024, outside the 1 May 2026 – 30 Apr 2027 window); the
    /// event's <c>deltaDays</c> is 1,0 and <c>newUsed</c> 2,0.</para>
    ///
    /// <para><b>Red conditions (Docker; argued).</b> On the base <c>87b26453</c> the ensure-row insert seeds
    /// <c>used = 0</c> (<c>EntitlementBalanceRepository.cs:400-415</c>) and then adds the delta, so the row
    /// ends at 1,0 in both cases: <c>Assert.Equal(3.0m, used)</c> (VACATION) and
    /// <c>Assert.Equal(2.0m, used)</c> (SPECIAL_HOLIDAY) trip, and the event's <c>newUsed</c> (1,0) trips
    /// beside them. The same assertions catch a window that is too long (VACATION run to 31 Dec 2026 would
    /// add Thu 1 Oct 2026: 4,0) or too early (SPECIAL_HOLIDAY from 1 Jan 2026 would add Thu 30 Apr 2026:
    /// 3,0).</para>
    /// </summary>
    [Theory]
    [InlineData(Vacation)]
    [InlineData(SpecialHoliday)]
    public async Task MissingRow_BookingSeedsFromCurrentRows(string entitlementType)
    {
        var emp = await SeedFixtureAsync();
        Assert.Null(await ReadUsedAsync(emp, entitlementType, EntitlementYear)); // premise: no row yet

        var (date, absenceType, expectedUsed) = entitlementType == Vacation
            ? (Fri12, Vacation, 3.0m)
            : (Tue9, SpecialHolidayAllowance, 2.0m);

        var rsp = await SaveAsync(emp, date, absenceType, 7.4m);
        await AssertStatusAsync(rsp, HttpStatusCode.OK, $"booking {absenceType} on {date}");

        Assert.Equal(expectedUsed, await ReadUsedAsync(emp, entitlementType, EntitlementYear));

        var evt = await ReadAdjustedEventAsync(emp, entitlementType);
        Assert.Equal(1.0m, evt.GetProperty("deltaDays").GetDecimal());
        Assert.Equal(expectedUsed, evt.GetProperty("newUsed").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // AC-BS-2 — a reduction on a missing row (AdjustUsedAsync)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>AC-BS-2 — kind F (CI).</b> Re-saving Wed 10 June as 3,7 h (0,5 day) while the (VACATION, 2025)
    /// row is missing: 200; <c>used</c> is 1,5 (Wed 10's 0,5 plus Thu 11's 1,0), not −0,5; the event's
    /// <c>deltaDays</c> is −0,5 and <c>newUsed</c> 1,5.
    ///
    /// <para><b>Red conditions (Docker; argued).</b> On the base the reduction path
    /// (<c>AdjustUsedAsync</c>, ensure-row insert at <c>EntitlementBalanceRepository.cs:135-150</c>) seeds
    /// <c>used = 0</c> and adds −0,5, so the row ends at −0,5: <c>Assert.Equal(1.5m, used)</c> trips, and
    /// so does the event's <c>newUsed</c>.</para>
    /// </summary>
    [Fact]
    public async Task MissingRow_ReductionSeedsFromCurrentRows()
    {
        var emp = await SeedFixtureAsync();
        Assert.Null(await ReadUsedAsync(emp, Vacation, EntitlementYear)); // premise: no row yet

        var rsp = await SaveAsync(emp, Wed10, Vacation, 3.7m);
        await AssertStatusAsync(rsp, HttpStatusCode.OK, "re-saving Wed 10 June as a half day");

        Assert.Equal(1.5m, await ReadUsedAsync(emp, Vacation, EntitlementYear));

        var evt = await ReadAdjustedEventAsync(emp, Vacation);
        Assert.Equal(-0.5m, evt.GetProperty("deltaDays").GetDecimal());
        Assert.Equal(1.5m, evt.GetProperty("newUsed").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // AC-BS-5 — an existing row is never re-seeded
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>AC-BS-5 — kind R (CI): an existing behaviour kept.</b> With the (VACATION, 2025) row present and
    /// its <c>used</c> set by SQL to 7,0 (a value that is neither the Σ of the current rows, 2,0 before the
    /// booking and 3,0 after, nor the old delta-only 1,0), booking Fri 12 June adds only its delta: 8,0.
    ///
    /// <para><b>Red conditions.</b> Green on the base and after (the ensure-row insert does nothing on an
    /// existing row, <c>ON CONFLICT … DO NOTHING</c>, and the delta is added). It can fail: a writer that
    /// re-seeded an existing row from the Σ would end at 3,0 (or 2,0 plus the delta), and
    /// <c>Assert.Equal(8.0m, used)</c> trips.</para>
    /// </summary>
    [Fact]
    public async Task ExistingRow_AddsOnlyTheDelta()
    {
        var emp = await SeedFixtureAsync();
        await InsertBalanceRowAsync(emp, Vacation, EntitlementYear, totalQuota: 25m, used: 7.0m);

        var rsp = await SaveAsync(emp, Fri12, Vacation, 7.4m);
        await AssertStatusAsync(rsp, HttpStatusCode.OK, "booking Fri 12 June onto an existing row");

        Assert.Equal(8.0m, await ReadUsedAsync(emp, Vacation, EntitlementYear));
    }

    // ════════════════════════════════════════════════════════════════════════
    // The shared fixture
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The shared fixture (see the class remarks). Every setup step asserts its own result, so a
    /// premise that does not hold fails here and names itself instead of tripping a later assertion.</summary>
    private async Task<string> SeedFixtureAsync()
    {
        foreach (var d in new[] { Wed10, Thu11, Fri12, Tue9, Mon8, PreviousFerieaarDay, NextFerieaarDay, EarlySpecialHolidayDay })
            Assert.True(d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday), $"{d} must be a weekday (S138).");

        var emp = "emp_s148_bs_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_fx.ConnectionString, emp, OrgId, "HK", "OK24");

        // Real saves: Wed 10 twice (the second retires the first), Thu 11 once.
        await AssertSetupOkAsync(await SaveAsync(emp, Wed10, Vacation, 7.4m), "VACATION Wed 10 June");
        await AssertSetupOkAsync(await SaveAsync(emp, Wed10, Vacation, 7.4m), "VACATION Wed 10 June, saved again");
        await AssertSetupOkAsync(await SaveAsync(emp, Thu11, Vacation, 7.4m), "VACATION Thu 11 June");

        // The balance row the saves created goes (exactly one row), leaving the rows without a record.
        Assert.Equal(1, await DeleteBalanceRowAsync(emp, Vacation, EntitlementYear));

        // The rows that must NOT count toward the seeds (feriedage 1,0, source SKEMA).
        await SeedSkemaAbsenceAsync(emp, PreviousFerieaarDay, Vacation, 7.4m, 1.0m);
        await SeedSkemaAbsenceAsync(emp, NextFerieaarDay, Vacation, 7.4m, 1.0m);
        await SeedSkemaAbsenceAsync(emp, Mon8, SpecialHolidayAllowance, 7.4m, 1.0m);
        await SeedSkemaAbsenceAsync(emp, EarlySpecialHolidayDay, SpecialHolidayAllowance, 7.4m, 1.0m);

        // Premises the expected values rest on: the 2025 ferieår holds Wed 10 and Thu 11 as current rows
        // worth 1,0 each (HK, 7,4 h on a 7,4 h norm), Wed 10 also holds exactly one retired row, and no
        // (SPECIAL_HOLIDAY, 2025) row exists.
        Assert.Equal(2.0m, await SumCurrentFeriedageAsync(
            emp, Vacation, new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31)));
        Assert.Equal(1L, await CountRetiredRowsAsync(emp, Wed10, Vacation));
        Assert.Null(await ReadUsedAsync(emp, SpecialHoliday, EntitlementYear));
        return emp;
    }

    // ════════════════════════════════════════════════════════════════════════
    // HTTP helpers
    // ════════════════════════════════════════════════════════════════════════

    private Task<HttpResponseMessage> SaveAsync(string emp, DateOnly date, string absenceType, decimal hours)
        => ClientWith(MintEmployeeToken(emp)).PostAsJsonAsync(
            $"/api/skema/{emp}/save",
            new
            {
                year = date.Year,
                month = date.Month,
                absences = new[]
                {
                    new { date = date.ToString("yyyy-MM-dd"), absenceType, hours },
                },
            });

    private static async Task AssertSetupOkAsync(HttpResponseMessage rsp, string what)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"setup save ({what}) must answer 200 before the save under test; got {(int)rsp.StatusCode}: {raw}");
    }

    private static async Task AssertStatusAsync(HttpResponseMessage rsp, HttpStatusCode expected, string what)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == expected,
            $"{what}: expected {(int)expected}, got {(int)rsp.StatusCode}: {raw}");
    }

    private HttpClient ClientWith(string bearer)
    {
        var client = _fx.Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private static string MintEmployeeToken(string actorId)
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.Employee,
            agreementCode: "HK", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
    }

    // ════════════════════════════════════════════════════════════════════════
    // SQL seeding and reads (literal statements only: a non-constant command text trips CA2100)
    // ════════════════════════════════════════════════════════════════════════

    private async Task SeedSkemaAbsenceAsync(
        string emp, DateOnly date, string absenceType, decimal hours, decimal feriedage)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage,
                 agreement_code, ok_version, occurred_at, actor_id, actor_role, outbox_id, source)
            VALUES (gen_random_uuid(), @e, @d, @t, @h, @f, 'HK', @ok, NOW(), 'test-seed', 'Employee', -1, 'SKEMA')
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        cmd.Parameters.AddWithValue("t", absenceType);
        cmd.Parameters.AddWithValue("h", hours);
        cmd.Parameters.AddWithValue("f", feriedage);
        cmd.Parameters.AddWithValue("ok", OkVersionResolver.ResolveVersion(date));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> DeleteBalanceRowAsync(string emp, string entitlementType, int year)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM entitlement_balances WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y",
            conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        return await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertBalanceRowAsync(string emp, string entitlementType, int year, decimal totalQuota, decimal used)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_balances
                (balance_id, employee_id, entitlement_type, entitlement_year, total_quota, used, planned, carryover_in, updated_at)
            VALUES (gen_random_uuid(), @e, @t, @y, @q, @u, 0, 0, NOW())
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("q", totalQuota);
        cmd.Parameters.AddWithValue("u", used);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary><c>entitlement_balances.used</c>, or null when the row does not exist.</summary>
    private async Task<decimal?> ReadUsedAsync(string emp, string entitlementType, int year)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT used FROM entitlement_balances WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y",
            conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToDecimal(result);
    }

    /// <summary>The Σ of the current (not retired) VACATION rows' stored <c>feriedage</c> in a window: the
    /// fixture's own premise, read from the table.</summary>
    private async Task<decimal> SumCurrentFeriedageAsync(string emp, string entitlementType, DateOnly from, DateOnly to)
    {
        Assert.Equal(Vacation, entitlementType); // the premise read is written for the VACATION rows only
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COALESCE(SUM(COALESCE(feriedage, 0)), 0) FROM absences_projection
            WHERE employee_id = @e AND absence_type = 'VACATION'
              AND date >= @from AND date <= @to AND superseded_by_outbox_id IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> CountRetiredRowsAsync(string emp, DateOnly date, string absenceType)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM absences_projection
            WHERE employee_id = @e AND date = @d AND absence_type = @t AND superseded_by_outbox_id IS NOT NULL
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        cmd.Parameters.AddWithValue("t", absenceType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>The latest <c>EntitlementBalanceAdjusted</c> payload of the employee's stream for one
    /// entitlement type (the save under test is the last to write one of that type).</summary>
    private async Task<JsonElement> ReadAdjustedEventAsync(string emp, string entitlementType)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @s AND event_type = 'EntitlementBalanceAdjusted'
              AND event_payload->>'entitlementType' = @t
            ORDER BY outbox_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("s", $"employee-{emp}");
        cmd.Parameters.AddWithValue("t", entitlementType);
        var result = await cmd.ExecuteScalarAsync();
        Assert.False(result is null or DBNull, $"expected an EntitlementBalanceAdjusted({entitlementType}) event for {emp}.");
        return JsonDocument.Parse((string)result!).RootElement.Clone();
    }

    // ════════════════════════════════════════════════════════════════════════
    // The shared container + host (one per class; facts isolate by their own employee ids)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>One Postgres container and one booted API for the class: the rule-engine seam stubbed by
    /// the real <c>EntitlementValidationRule</c> (without it the save fails closed) and the clock pinned
    /// (PAT-008) in ONE <c>ConfigureTestServices</c>, booted once BEFORE any fixture is seeded (a derived
    /// host's first <c>CreateClient()</c> re-runs the startup seeders). The settlement job is not run
    /// (<c>withoutLeaverJob</c>): it would race the fixture's current rows with year-end settlements.</summary>
    public sealed class Harness : IAsyncLifetime
    {
        private TestFixtures.DockerHarness _harness = null!;
        private StatsTidWebApplicationFactory _factory = null!;

        public WebApplicationFactory<Program> Host { get; private set; } = null!;
        public string ConnectionString { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            _harness = await TestFixtures.DockerHarness.StartAsync();
            ConnectionString = _harness.ConnectionString;
            await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(ConnectionString);
            _factory = new StatsTidWebApplicationFactory(ConnectionString, withoutLeaverJob: true);
            Host = _factory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory());
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(F));
                }));
            _ = Host.CreateClient(); // boot the startup seeders now; every fact seeds after this
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
