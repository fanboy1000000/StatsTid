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
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// <b>S148 / TASK-14842-T (QUAL-196; owner Q1 and Q4, 2026-11-11) — a balance record the profile
/// recalculation creates starts at what was booked and at the year's own entitlement.</b> AC-BS-3 of
/// addendum A1 § 4.
///
/// <para><b>Plain language.</b> When HR backdates a change of working hours, the system re-values every
/// registered holiday day inside the corrected interval against the new daily norm (a day booked at 7,4 h
/// that was one day is now two at a 3,7 h norm) and adjusts the year's balance record. If that record is
/// missing, the recalculation creates it. Before the change it created it with <c>total_quota</c> 0 and
/// <c>used</c> equal to the change alone, so the team overview showed "of 0 days" and a remaining figure
/// that stayed wrong. Now the new record starts at the sum of the year's current registered days (read after
/// the recalculation has re-valued them, so a day that had no stored value counts at its new value) and at
/// the entitlement in force on the first day of its own holiday year: read the way the Skema save reads it
/// (the agreement and the OK version at the year start), never the live row, which may have been superseded
/// since, and pro-rated by the NEW fraction where that year-start row says so.</para>
///
/// <para><b>Expected values come from the spec</b> (addendum A1 § 4 AC-BS-3; HK's VACATION annual quota 25,
/// flat, <c>init.sql</c> seeds), never from what the code returns. The employee is HK, full time, with a
/// backdated change to fraction 0,5 from Mon 1 June 2026; the half-time norm is 3,7 h, so 7,4 h is 2,0 days.
/// The (VACATION, 2025) row covers 1 Sep 2025 – 31 Aug 2026; the year-start key is (VACATION, HK, OK24) at
/// Mon 1 Sep 2025 (<c>OkVersionResolver</c>: OK24 to 31 Mar 2026), and the live row for a June 2026 date is
/// (VACATION, HK, OK26).</para>
///
/// <para><b>Isolation of the changed config rows.</b> Every fact owns its database (a fresh container per
/// theory row, <see cref="IAsyncLifetime"/>), so cases (c) and (d) change the shared seeded HK rows without
/// reaching any other fact. The settlement job is not run (<c>withoutLeaverJob</c>) and the clock is pinned
/// (PAT-008) at <see cref="F"/>.</para>
///
/// <para><b>Docker-gated; CI-pending, never locally green.</b> Docker does not run on the owner's machine.
/// Each case names its kind and its "Red conditions" on the base <c>87b26453</c>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RevaluationBalanceSeedTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY02";
    private const string LeaderId = "t14842_leader";
    private const string Vacation = "VACATION";
    private const int EntitlementYear = 2025;

    /// <summary>The pinned "today" (PAT-008): Mon 2026-06-15, after the backdated change's 1 June and on
    /// the OK26 side of the 2026-04-01 cutover.</summary>
    private static readonly DateOnly F = new(2026, 6, 15);

    private static readonly DateOnly ChangeFrom = new(2026, 6, 1); // Mon
    private static readonly DateOnly Wed10 = new(2026, 6, 10);
    private static readonly DateOnly Thu11 = new(2026, 6, 11);
    private static readonly DateOnly YearStart = new(2025, 9, 1);  // Mon; year-start key OK24
    private static readonly DateOnly YearEnd = new(2026, 8, 31);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString, withoutLeaverJob: true);
        _host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory());
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(F));
            }));
        _ = _host.CreateClient(); // boot the startup seeders now; every fact seeds after this
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>AC-BS-3 — kind F (CI).</b> A backdated profile change to fraction 0,5 from Mon 1 June 2026 on an
    /// HK full-time employee whose (VACATION, 2025) balance row is missing. In every case the row exists
    /// afterwards with <c>used</c> equal to the Σ of the rows' <c>feriedage</c> read back from
    /// <c>absences_projection</c> AFTER the change (and pinned to its literal, below), not the
    /// revaluation's delta alone and not the stored-value seed plus the delta; <c>total_quota</c> the
    /// year-start quota, never 0; <c>EntitlementBalance.Remaining</c> (<c>TotalQuota + CarryoverIn − Used −
    /// Planned</c>, computed here from the row) equal to that total minus <c>used</c>; and the team
    /// overview's <c>ferieTotal</c> for June 2026 equal to the total.
    /// <list type="bullet">
    ///   <item><b>(a)</b> <c>VACATION</c> 7,4 saved on Wed 10 and Thu 11 June through the real save, then the
    ///     balance row deleted by SQL. Σ after = 2,0 + 2,0 = 4,0; <c>total_quota</c> 25.</item>
    ///   <item><b>(b)</b> one current <c>VACATION</c> row of 7,4 h on Wed 10 June with NULL
    ///     <c>feriedage</c> (SQL-seeded, <c>source = 'SKEMA'</c>) and no balance row. The Σ is read after the
    ///     overwrite, so the NULL row counts at its rewritten 2,0; a stored-value seed plus the delta would be
    ///     one day short (0 + 1,0). <c>total_quota</c> 25.</item>
    ///   <item><b>(c) the year-start row and the live row differ.</b> (a)'s fixture, with the
    ///     (VACATION, HK, OK26) row superseded by SQL from 25 to 26 effective Thu 1 Jan 2026 (the old row
    ///     closed at that date, one open row per key). The year-start key (VACATION, HK, OK24) at 1 Sep 2025
    ///     is still 25; the live row the helper reads for a June 2026 date, (VACATION, HK, OK26), is 26.
    ///     <c>total_quota</c> 25, <b>not 26</b>. The supersession sits on the OK26 key because one on OK24
    ///     would leave the live read at 25 too.</item>
    ///   <item><b>(d) pro-rated where configured.</b> (a)'s fixture, with <c>pro_rate_by_part_time = true</c>
    ///     set by SQL on the year-start row (VACATION, HK, OK24) only: the seed is 25 × the new fraction 0,5 =
    ///     12,5. Every OK26 read, live or dated, finds no pro-rating and gives 25.</item>
    /// </list>
    ///
    /// <para><b>Red conditions (Docker; argued).</b> On the base <c>87b26453</c> the revaluation's single
    /// upsert (<c>EntitlementBalanceRepository.cs:193-205</c>) creates the missing row with
    /// <c>total_quota = 0</c> and <c>used</c> = the delta: every case trips <c>Assert.Equal(25m / 12.5m,
    /// total_quota)</c> (0 is found), and (a), (c), (d) also trip on <c>used</c> (the delta alone, 2,0, not
    /// the Σ 4,0), (b) on <c>used</c> (the delta 1,0, not 2,0).
    /// <b>Wrong fixes this catches:</b> a seed from the live row gives 26 in (c) and, in (d), 25 (the OK26
    /// live row has no pro-rating); a year-start date read under the absence date's OK version (OK26 at
    /// 1 Sep 2025) gives 25 in (d), which (c) cannot catch (the OK26 row is still 25 on 1 Sep 2025); a
    /// <c>used</c> seeded from the stored values before the overwrite gives 0 for the NULL row in (b).</para>
    /// </summary>
    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("c")]
    [InlineData("d")]
    public async Task MissingRow_RevaluationSeedsFromCurrentRows(string caseId)
    {
        Assert.Equal(DayOfWeek.Monday, ChangeFrom.DayOfWeek);
        Assert.Equal(DayOfWeek.Wednesday, Wed10.DayOfWeek);
        Assert.Equal(DayOfWeek.Thursday, Thu11.DayOfWeek);
        Assert.Equal("OK24", OkVersionResolver.ResolveVersion(YearStart));
        Assert.Equal("OK26", OkVersionResolver.ResolveVersion(Wed10));

        var emp = await SeedEmployeeWithLeaderAsync();

        if (caseId == "b")
        {
            // One current row with no stored feriedage, and no balance row at all.
            await SeedNullFeriedageRowAsync(emp, Wed10, 7.4m);
            Assert.Equal(0m, await SumCurrentFeriedageAsync(emp));
        }
        else
        {
            await AssertSetupOkAsync(await SaveAsync(emp, Wed10, 7.4m), "VACATION Wed 10 June");
            await AssertSetupOkAsync(await SaveAsync(emp, Thu11, 7.4m), "VACATION Thu 11 June");
            Assert.Equal(2.0m, await SumCurrentFeriedageAsync(emp)); // premise: 1,0 each at the full-time norm
        }

        if (caseId == "c")
        {
            await SupersedeVacationQuotaAsync("HK", "OK26", from: new DateOnly(2026, 1, 1), newQuota: 26m);
            Assert.Equal(26m, await OpenQuotaAsync("HK", "OK26"));       // the live row for a June 2026 date
            Assert.Equal(25m, await DatedQuotaAsync("HK", "OK24", YearStart)); // the year-start key, untouched
        }
        else if (caseId == "d")
        {
            await SetProRateOnOpenRowAsync("HK", "OK24");
            Assert.Equal(25m, await OpenQuotaAsync("HK", "OK24"));       // the quota itself is unchanged
        }

        // The row the saves created goes (a, c, d); in (b) nothing created one. Either way: no row.
        await DeleteBalanceRowAsync(emp);
        Assert.Null(await ReadBalanceAsync(emp));

        // The backdated change: fraction 0,5 from Mon 1 June 2026.
        var admin = ClientWith(MintGlobalAdminToken());
        var version = await ReadProfileVersionAsync(admin, emp);
        var put = await PutProfileAsync(admin, emp, ChangeFrom, 0.500m, ifMatch: $"\"{version}\"");
        await AssertStatusAsync(put, HttpStatusCode.OK, "the backdated profile change");

        var balance = await ReadBalanceAsync(emp);
        Assert.True(balance is not null, "the revaluation must create the (VACATION, 2025) balance row.");
        var (totalQuota, used, carryoverIn, planned) = balance!.Value;

        // used: the Σ of the rows' feriedage read back AFTER the change, and its literal (half-time norm
        // 3,7 h: 7,4 h is 2,0 days; (a), (c), (d) hold two such days, (b) one).
        var sumAfter = await SumCurrentFeriedageAsync(emp);
        var expectedUsed = caseId == "b" ? 2.0m : 4.0m;
        Assert.Equal(expectedUsed, sumAfter);
        Assert.Equal(sumAfter, used);

        // total_quota: the year-start quota; (c) not the live 26; (d) 25 x the new fraction.
        var expectedTotal = caseId == "d" ? 12.5m : 25m;
        Assert.Equal(expectedTotal, totalQuota);

        // EntitlementBalance.Remaining = TotalQuota + CarryoverIn - Used - Planned (no carryover, nothing planned).
        Assert.Equal(0m, carryoverIn);
        Assert.Equal(0m, planned);
        Assert.Equal(expectedTotal - sumAfter, totalQuota + carryoverIn - used - planned);

        // The team overview serves the same total for June 2026.
        Assert.Equal(expectedTotal, await ReadTeamOverviewFerieTotalAsync(emp, 2026, 6));
    }

    // ════════════════════════════════════════════════════════════════════════
    // HTTP helpers
    // ════════════════════════════════════════════════════════════════════════

    private Task<HttpResponseMessage> SaveAsync(string emp, DateOnly date, decimal hours)
        => ClientWith(MintEmployeeToken(emp)).PostAsJsonAsync(
            $"/api/skema/{emp}/save",
            new
            {
                year = date.Year,
                month = date.Month,
                absences = new[]
                {
                    new { date = date.ToString("yyyy-MM-dd"), absenceType = "VACATION", hours },
                },
            });

    private static Task<HttpResponseMessage> PutProfileAsync(
        HttpClient admin, string emp, DateOnly effectiveFrom, decimal partTimeFraction, string ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{emp}")
        {
            Content = JsonContent.Create(new
            {
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
                partTimeFraction,
                position = (string?)null,
                employmentCategory = (string?)null,
            }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return admin.SendAsync(req);
    }

    private static async Task<long> ReadProfileVersionAsync(HttpClient admin, string emp)
    {
        var rsp = await admin.GetAsync($"/api/admin/employee-profiles/{emp}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return (await rsp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt64();
    }

    private async Task<decimal> ReadTeamOverviewFerieTotalAsync(string emp, int year, int month)
    {
        var client = ClientWith(MintLeaderToken());
        var rsp = await client.GetAsync($"/api/approval/team-overview?year={year}&month={month}");
        await AssertStatusAsync(rsp, HttpStatusCode.OK, "the team overview");
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var row = body.GetProperty("employees").EnumerateArray()
            .SingleOrDefault(r => r.GetProperty("employeeId").GetString() == emp);
        Assert.True(row.ValueKind == JsonValueKind.Object, $"the leader's team overview must list {emp}.");
        return row.GetProperty("ferieTotal").GetDecimal();
    }

    private static async Task AssertSetupOkAsync(HttpResponseMessage rsp, string what)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"setup save ({what}) must answer 200 before the change under test; got {(int)rsp.StatusCode}: {raw}");
    }

    private static async Task AssertStatusAsync(HttpResponseMessage rsp, HttpStatusCode expected, string what)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == expected,
            $"{what}: expected {(int)expected}, got {(int)rsp.StatusCode}: {raw}");
    }

    private HttpClient ClientWith(string bearer)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private static JwtTokenService TokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    private static string MintEmployeeToken(string actorId)
        => TokenService().GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.Employee,
            agreementCode: "HK", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });

    private static string MintLeaderToken()
        => TokenService().GenerateToken(
            employeeId: LeaderId, name: LeaderId, role: StatsTidRoles.LocalLeader,
            agreementCode: "HK", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, OrgId, "ORG_ONLY") });

    private static string MintGlobalAdminToken()
        => TokenService().GenerateToken(
            employeeId: "ADMIN_S148_BS", name: "S148 Balance Seed Admin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "HK",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    // ════════════════════════════════════════════════════════════════════════
    // SQL seeding and reads (literal statements only: a non-constant command text trips CA2100)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>An HK full-time employee on STY02 reporting PRIMARY to a LOCAL_LEADER on STY02: the shape
    /// of <c>TeamOverviewAggregateTests</c>, so the leader's team overview lists the employee (the roster
    /// is the designated-act set; a month with no period still yields a row).</summary>
    private async Task<string> SeedEmployeeWithLeaderAsync()
    {
        var emp = "emp_s148_rev_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, OrgId, "HK", "OK24");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, LeaderId, OrgId, "HK", "OK24");

        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
                VALUES (@leader, 'LOCAL_LEADER', @org, 'ORG_ONLY', 'TEST'),
                       (@emp,    'EMPLOYEE',     @org, 'ORG_ONLY', 'TEST')
                ON CONFLICT DO NOTHING
                """, conn);
            cmd.Parameters.AddWithValue("leader", LeaderId);
            cmd.Parameters.AddWithValue("emp", emp);
            cmd.Parameters.AddWithValue("org", OrgId);
            await cmd.ExecuteNonQueryAsync();
        }

        var rlRepo = new ReportingLineRepository(new DbConnectionFactory(_harness.ConnectionString));
        await rlRepo.AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = emp,
            ManagerId = LeaderId,
            OrganisationId = OrgId,
            Relationship = "PRIMARY",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });
        return emp;
    }

    private async Task SeedNullFeriedageRowAsync(string emp, DateOnly date, decimal hours)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage,
                 agreement_code, ok_version, occurred_at, actor_id, actor_role, outbox_id, source)
            VALUES (gen_random_uuid(), @e, @d, 'VACATION', @h, NULL, 'HK', @ok, NOW(), 'test-seed', 'Employee', -1, 'SKEMA')
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        cmd.Parameters.AddWithValue("h", hours);
        cmd.Parameters.AddWithValue("ok", OkVersionResolver.ResolveVersion(date));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>The Σ of the employee's current VACATION rows' stored feriedage in the 2025 ferieår
    /// (1 Sep 2025 – 31 Aug 2026), a NULL counting as 0 (the table as it stands).</summary>
    private async Task<decimal> SumCurrentFeriedageAsync(string emp)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COALESCE(SUM(COALESCE(feriedage, 0)), 0) FROM absences_projection
            WHERE employee_id = @e AND absence_type = 'VACATION'
              AND date >= @from AND date <= @to AND superseded_by_outbox_id IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("from", YearStart);
        cmd.Parameters.AddWithValue("to", YearEnd);
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync());
    }

    private async Task DeleteBalanceRowAsync(string emp)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM entitlement_balances WHERE employee_id = @e AND entitlement_type = 'VACATION' AND entitlement_year = @y",
            conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("y", EntitlementYear);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(decimal TotalQuota, decimal Used, decimal CarryoverIn, decimal Planned)?> ReadBalanceAsync(string emp)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT total_quota, used, carryover_in, planned FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = 'VACATION' AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("y", EntitlementYear);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return (reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3));
    }

    /// <summary>Supersedes the open VACATION row of (agreement, OK version): the old row is closed at
    /// <paramref name="from"/> and a copy with the new quota opens there (one open row per natural key; a
    /// history row per key and <c>effective_from</c>).</summary>
    private async Task SupersedeVacationQuotaAsync(string agreementCode, string okVersion, DateOnly from, decimal newQuota)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var close = new NpgsqlCommand(
            """
            UPDATE entitlement_configs SET effective_to = @from
            WHERE entitlement_type = 'VACATION' AND agreement_code = @a AND ok_version = @ok AND effective_to IS NULL
            """, conn, tx))
        {
            close.Parameters.AddWithValue("from", from);
            close.Parameters.AddWithValue("a", agreementCode);
            close.Parameters.AddWithValue("ok", okVersion);
            Assert.Equal(1, await close.ExecuteNonQueryAsync());
        }
        await using (var open = new NpgsqlCommand(
            """
            INSERT INTO entitlement_configs
                (entitlement_type, agreement_code, ok_version, annual_quota, accrual_model, reset_month,
                 carryover_max, pro_rate_by_part_time, is_per_episode, min_age, description, effective_from, full_day_only)
            SELECT entitlement_type, agreement_code, ok_version, @quota, accrual_model, reset_month,
                   carryover_max, pro_rate_by_part_time, is_per_episode, min_age, description, @from, full_day_only
            FROM entitlement_configs
            WHERE entitlement_type = 'VACATION' AND agreement_code = @a AND ok_version = @ok AND effective_to = @from
            """, conn, tx))
        {
            open.Parameters.AddWithValue("quota", newQuota);
            open.Parameters.AddWithValue("from", from);
            open.Parameters.AddWithValue("a", agreementCode);
            open.Parameters.AddWithValue("ok", okVersion);
            Assert.Equal(1, await open.ExecuteNonQueryAsync());
        }
        await tx.CommitAsync();
    }

    /// <summary>Sets <c>pro_rate_by_part_time = true</c> on the open VACATION row of one key only (the
    /// open-seed-row update of <c>DatedQuotaAnchorTests.SetEntitlementColumnAsync</c>).</summary>
    private async Task SetProRateOnOpenRowAsync(string agreementCode, string okVersion)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE entitlement_configs SET pro_rate_by_part_time = TRUE
            WHERE entitlement_type = 'VACATION' AND agreement_code = @a AND ok_version = @ok AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync()); // sentinel: the seed row must exist
    }

    private async Task<decimal> OpenQuotaAsync(string agreementCode, string okVersion)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT annual_quota FROM entitlement_configs
            WHERE entitlement_type = 'VACATION' AND agreement_code = @a AND ok_version = @ok AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync());
    }

    private async Task<decimal> DatedQuotaAsync(string agreementCode, string okVersion, DateOnly asOf)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT annual_quota FROM entitlement_configs
            WHERE entitlement_type = 'VACATION' AND agreement_code = @a AND ok_version = @ok
              AND effective_from <= @d AND (effective_to IS NULL OR effective_to > @d)
            """, conn);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("d", asOf);
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync());
    }

    private sealed class RuleEngineStubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new EntitlementValidationStubHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("http://rule-engine:8080"),
        };
    }
}
