using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// <b>S148 / TASK-14842-T (QUAL-196; owner Q1 and Q4, 2026-11-11; cycle-3 ruling R1) — a carryover that
/// creates next year's balance record starts it at what is already booked there and at next year's own
/// entitlement.</b> AC-BS-4 of addendum A1 § 4.
///
/// <para><b>Plain language.</b> A year-end close that transfers holiday days into the next holiday year
/// (a §21 agreement) writes them to next year's balance record, creating it if it is missing. Before the
/// change that new record started at 0 used (although next year already held registered days) and at the
/// <i>closing</i> year's entitlement, so a raise that took effect when the new year began was ignored. Now it
/// starts at the sum of next year's current registered days and at the entitlement in force on next year's
/// first day. That value is neither the closing year's, nor the live row's: a close always runs months into
/// the new year, when the entitlement may have been raised again. The same applies when HR rescues days
/// from forfeiture under §22 (feriehindring) on a row whose close transferred nothing, so the rescue creates
/// the next-year record.</para>
///
/// <para><b>The years (contract, 14842-T BC-1; addendum A1 § 4).</b> N = 2024 (ferieår 1 Sep 2024 –
/// 31 Aug 2025), N+1 = 2025 (1 Sep 2025 – 31 Aug 2026). Year N+1's start key is (VACATION, HK, OK24) at
/// Mon 1 Sep 2025 (<c>OkVersionResolver</c>: OK24 to 31 Mar 2026). Its supersessions take effect Mon 1 Sep
/// 2025 (25 to 26) and Mon 1 Dec 2025 (26 to 27); the settled year's anchor, 1 Sep 2024, still reads 25.
/// <b>The close's clock (its "today") is after 1 December 2025</b>: the host's clock is pinned (PAT-008) at
/// <see cref="F"/>, Mon 15 June 2026, which the fact asserts, so the 26 to 27 row is the LIVE open row when
/// the close runs and a live-row seed would show 27. Year N+1's current row is <c>VACATION</c>,
/// <c>source = 'SKEMA'</c>, <c>feriedage</c> 1,0 on Wed 10 June 2026. Two rows that must not count: Fri 29 Aug
/// 2025 (year 2024) and Thu 1 Oct 2026 (year 2026), each 1,0.</para>
///
/// <para><b>Isolation of the changed config rows.</b> Every fact owns its database (a fresh container per
/// theory row, <see cref="IAsyncLifetime"/>), so (b) and (c) supersede the shared seeded HK rows without
/// reaching any other fact. The settlement job is not run (<c>withoutLeaverJob</c>): it would settle year
/// 2024 itself, with the transfer agreement the fact seeds.</para>
///
/// <para><b>Docker-gated; CI-pending, never locally green.</b> Docker does not run on the owner's machine.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class CarryoverBalanceSeedTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string Vacation = "VACATION";
    private const string YearEnd = "YEAR_END";
    private const int N = 2024;
    private const int NextYear = N + 1;
    private const string Reason = "Langtidssygdom hele ferieåret (§22 feriehindring)";

    /// <summary>The close's clock: Mon 2026-06-15, after 1 Dec 2025 (the 26 to 27 supersession) and after
    /// the settled year's 31 Dec 2025 boundary.</summary>
    private static readonly DateOnly F = new(2026, 6, 15);

    private static readonly DateOnly NextYearStart = new(2025, 9, 1);  // Mon; year N+1's start key is OK24
    private static readonly DateOnly SecondRaise = new(2025, 12, 1);   // Mon
    private static readonly DateOnly SettledYearAnchor = new(2024, 9, 1);
    private static readonly DateOnly Wed10 = new(2026, 6, 10);
    private static readonly DateOnly PreviousFerieaarDay = new(2025, 8, 29); // Fri; VACATION year 2024
    private static readonly DateOnly NextFerieaarDay = new(2026, 10, 1);     // Thu; VACATION year 2026

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString, withoutLeaverJob: true);
        _host = _factory.WithFixedToday(F);
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
    /// <b>AC-BS-4 — kind F (CI).</b> A <c>VACATION</c> settlement of year N = 2024 that transfers days into
    /// year N+1 = 2025, which holds a current row and no balance row. In each case the created (VACATION,
    /// 2025) row has <c>used</c> 1,0 (the Σ of year N+1's current rows: Wed 10 June; not 0, and not 2,0 or
    /// 3,0, which Fri 29 Aug 2025 and Thu 1 Oct 2026 would add), <c>carryover_in</c> the transfer, and
    /// <c>total_quota</c> year N+1's year-start quota.
    /// <list type="bullet">
    ///   <item><b>(a)</b> the YEAR_END close (a §21 agreement of 5 days; <c>carryover_in</c> 5), the quota the
    ///     same in both years: <c>total_quota</c> 25.</item>
    ///   <item><b>(b) the quota re-dated across the year boundary.</b> The close again, with the VACATION row
    ///     under year N+1's start key (VACATION, HK, OK24) superseded by SQL from 25 to 26 effective Mon 1 Sep
    ///     2025 AND from 26 to 27 effective Mon 1 Dec 2025, before the close runs (a year-end close always
    ///     runs months into year N+1, so the live row and the year-start row differ). The settled year still
    ///     reads 25 at its anchor (the snapshot's <c>annualQuota</c>, asserted) and year N+1 starts at 26:
    ///     <c>total_quota</c> <b>26</b>, neither the settled year's 25 nor the live row's 27.</item>
    ///   <item><b>(c)</b> (b)'s re-dating through the §22 feriehindring rescue, on a PENDING_REVIEW row whose
    ///     close transferred nothing, so the close wrote no next-year row and the rescue creates it: 15
    ///     flagged, 10 rescued, <c>carryover_in</c> 10, <c>total_quota</c> <b>26</b>. The seeded snapshot
    ///     carries <c>resetMonth</c> 9 (the rescue now needs it) and the settled year's quota 25.</item>
    /// </list>
    ///
    /// <para><b>Fix-up 1 cases (14842 cycle 1, Codex B-1 = Fable W-2).</b>
    /// <b>(d) kind F:</b> the year-start row is in force but the key has no OPEN row: 25 to 26 effective 1 Sep
    /// 2025, then the 26 row closed (<c>effective_to</c> 15 Jan 2026, before the clock, as the admin
    /// soft-delete stamps it). The close creates the row at <c>total_quota</c> 26, <c>used</c> 1,0,
    /// <c>carryover_in</c> 5. Red on <c>2dcac3e3</c> and <c>bbbd5eea</c>: no open row meant the fallback to the
    /// settled year's 25, so <c>Assert.Equal(26m, total_quota)</c> trips.
    /// <b>(f) kind F:</b> (d) through the §22 rescue (carryover_in 10); red for the same reason.
    /// <b>(e) kind R:</b> the OA-5 companion: no row in force on 1 Sep 2025 at all (the original row closed
    /// 1 Jun 2025), so the seed falls back to the settled year's 25. The old code also gave 25 here, so it is
    /// green before and after; it fails if the fallback is lost (0, or a throw).</para>
    ///
    /// <para><b>Red conditions (Docker; argued).</b> On the base <c>87b26453</c> the carryover creates the
    /// missing row with <c>used = 0</c> and <c>total_quota = seedQuota</c> where the YEAR_END close passes the
    /// settled year's <c>snapshot.AnnualQuota</c> (<c>VacationSettlementService.cs:451-453</c>) and the
    /// rescue <c>feriehindringSnapshot?.AnnualQuota ?? 0m</c> (<c>VacationSettlementEndpoints.cs:858-861</c>).
    /// Every case trips <c>Assert.Equal(1.0m, used)</c> (0 is found); (b) and (c) also trip
    /// <c>Assert.Equal(26m, total_quota)</c> (the settled year's 25 is found).
    /// <b>Wrong fixes this catches:</b> a seed left at the settled year's quota gives 25 in (b) and (c); a seed
    /// from the live row gives 27 there (the 26 to 27 row is open when the close runs); a fix made only in the
    /// close leaves (c) red, and one made only in the rescue leaves (a) and (b) red on <c>used</c>.</para>
    /// </summary>
    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("c")]
    [InlineData("d")]
    [InlineData("e")]
    [InlineData("f")]
    public async Task MissingNextYearRow_CarryoverSeedsFromCurrentRows(string caseId)
    {
        // The years and dates are contract: weekdays (S138) and the close's clock after 1 Dec 2025.
        Assert.Equal(DayOfWeek.Monday, NextYearStart.DayOfWeek);
        Assert.Equal(DayOfWeek.Monday, SecondRaise.DayOfWeek);
        foreach (var d in new[] { Wed10, PreviousFerieaarDay, NextFerieaarDay })
            Assert.True(d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday), $"{d} must be a weekday (S138).");
        Assert.True(F > SecondRaise, "the close's clock must be after 1 December 2025.");
        Assert.Equal("OK24", OkVersionResolver.ResolveVersion(NextYearStart));
        Assert.Equal("OK24", OkVersionResolver.ResolveVersion(SettledYearAnchor));

        var emp = await SeedEmployeeAsync();

        // Year N+1's current row, and the two rows that must not count toward its Σ (feriedage 1,0, SKEMA).
        await SeedSkemaAbsenceAsync(emp, Wed10, 7.4m, 1.0m);
        await SeedSkemaAbsenceAsync(emp, PreviousFerieaarDay, 7.4m, 1.0m);
        await SeedSkemaAbsenceAsync(emp, NextFerieaarDay, 7.4m, 1.0m);

        if (caseId is "b" or "c")
        {
            await SupersedeVacationQuotaAsync("HK", "OK24", NextYearStart, 26m);
            await SupersedeVacationQuotaAsync("HK", "OK24", SecondRaise, 27m);
            Assert.Equal(27m, await DatedQuotaAsync("HK", "OK24", null));            // the live (open) row
            Assert.Equal(26m, await DatedQuotaAsync("HK", "OK24", NextYearStart));   // year N+1's year-start row
            Assert.Equal(25m, await DatedQuotaAsync("HK", "OK24", SettledYearAnchor)); // the settled year's anchor
        }
        else if (caseId is "d" or "f")
        {
            // The year-start row is in force but the key has no open row: 25 to 26 effective 1 Sep 2025, then
            // the 26 row closed after that and before the close's clock, as the admin soft-delete stamps it.
            await SupersedeVacationQuotaAsync("HK", "OK24", NextYearStart, 26m);
            await CloseOpenRowAsync("HK", "OK24", new DateOnly(2026, 1, 15));
            Assert.False(await HasOpenRowAsync("HK", "OK24"), "premise: the key has no open row.");
            Assert.Equal(26m, await DatedQuotaAsync("HK", "OK24", NextYearStart));
            Assert.Equal(25m, await DatedQuotaAsync("HK", "OK24", SettledYearAnchor));
        }
        else if (caseId == "e")
        {
            // No row in force on 1 Sep 2025 at all (the original row closed before it); the settled year's
            // anchor, 1 Sep 2024, still reads 25.
            await CloseOpenRowAsync("HK", "OK24", new DateOnly(2025, 6, 1));
            Assert.False(await HasOpenRowAsync("HK", "OK24"), "premise: the key has no open row.");
            Assert.Null(await DatedQuotaOrNullAsync("HK", "OK24", NextYearStart));
            Assert.Equal(25m, await DatedQuotaAsync("HK", "OK24", SettledYearAnchor));
        }

        // Premise: no balance row in either year.
        Assert.Null(await ReadBalanceAsync(emp, N));
        Assert.Null(await ReadBalanceAsync(emp, NextYear));

        decimal expectedCarryover;
        if (caseId is not ("c" or "f"))
        {
            await SeedTransferAgreementAsync(emp, N, 5m);
            var outcome = await SettleAsync(emp, N);
            Assert.True(outcome.DidSettle);
            Assert.Equal(5m, outcome.Partition!.TransferDays);
            expectedCarryover = 5m;

            // The settled year's snapshot reads the quota at its own anchor: 25 in every case.
            Assert.Equal(25m, await ReadSnapshotAnnualQuotaAsync(emp, N));
        }
        else
        {
            await SeedPendingReviewRowAsync(emp, N, forfeitDays: 15m, transferDays: 0m);
            var rsp = await ResolveFeriehindringAsync(emp, N, impededDays: 10m, ifMatch: "\"1\"");
            var raw = await rsp.Content.ReadAsStringAsync();
            Assert.True(rsp.StatusCode == HttpStatusCode.OK,
                $"the feriehindring rescue must answer 200; got {(int)rsp.StatusCode}: {raw}");
            expectedCarryover = 10m;
        }

        var row = await ReadBalanceAsync(emp, NextYear);
        Assert.True(row is not null, "the carryover must create the (VACATION, 2025) balance row.");
        var (totalQuota, used, carryoverIn) = row!.Value;

        Assert.Equal(1.0m, used);                         // the Σ of year N+1's current rows, not 0
        Assert.Equal(expectedCarryover, carryoverIn);     // the transfer (in (c), the rescued days)
        Assert.Equal(caseId is "a" or "e" ? 25m : 26m, totalQuota); // year N+1's year-start quota (e: OA-5 fallback)
    }

    // ════════════════════════════════════════════════════════════════════════
    // Drivers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>One YEAR_END settlement pass in its OWN ReadCommitted transaction, committed: the shape of
    /// <c>VacationSettlementServiceTests.SettleAsync</c>.</summary>
    private async Task<SettlementOutcome> SettleAsync(string emp, int year)
    {
        var service = _host.Services.GetRequiredService<VacationSettlementService>();
        await using var conn = _host.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var outcome = await service.SettleAsync(emp, Vacation, year, YearEnd, conn, tx);
        await tx.CommitAsync();
        return outcome;
    }

    private async Task<HttpResponseMessage> ResolveFeriehindringAsync(string emp, int year, decimal impededDays, string ifMatch)
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = svc.GenerateToken(
            employeeId: "hr_s148_bs", name: "hr_s148_bs", role: StatsTidRoles.LocalHR,
            agreementCode: "HK", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, OrgId, "ORG_ONLY") });
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/vacation-settlements/{emp}/{Vacation}/{year}/resolve")
        {
            Content = JsonContent.Create(new { disposition = "FERIEHINDRING", impededDays, reason = Reason }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    // ════════════════════════════════════════════════════════════════════════
    // SQL seeding and reads (literal statements only: a non-constant command text trips CA2100)
    // ════════════════════════════════════════════════════════════════════════

    private async Task<string> SeedEmployeeAsync()
    {
        var emp = "emp_s148_co_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, OrgId, "HK", "OK24");
        return emp;
    }

    private async Task SeedSkemaAbsenceAsync(string emp, DateOnly date, decimal hours, decimal feriedage)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage,
                 agreement_code, ok_version, occurred_at, actor_id, actor_role, outbox_id, source)
            VALUES (gen_random_uuid(), @e, @d, 'VACATION', @h, @f, 'HK', @ok, NOW(), 'test-seed', 'Employee', -1, 'SKEMA')
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        cmd.Parameters.AddWithValue("h", hours);
        cmd.Parameters.AddWithValue("f", feriedage);
        cmd.Parameters.AddWithValue("ok", OkVersionResolver.ResolveVersion(date));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedTransferAgreementAsync(string emp, int year, decimal transferDays)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_transfer_agreements
                (employee_id, entitlement_year, entitlement_type, transfer_days, agreement_date, recorded_by, version)
            VALUES (@e, @y, 'VACATION', @days, @date, 'test-seed-hr', 1)
            ON CONFLICT (employee_id, entitlement_year, entitlement_type)
                DO UPDATE SET transfer_days = EXCLUDED.transfer_days
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("days", transferDays);
        // Within the §21 deadline of the closed year (31 Dec of the ferieår-end year, E+1).
        cmd.Parameters.AddWithValue("date", new DateOnly(year + 1, 6, 30));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Direct settlement-row seed (the <c>FeriehindringResolutionTests</c> convention): a
    /// YEAR_END PENDING_REVIEW row whose close transferred nothing, with a minimal valid snapshot carrying
    /// the settled year's quota 25 (the wrong seed) and <c>resetMonth</c> 9.</summary>
    private async Task SeedPendingReviewRowAsync(string emp, int year, decimal forfeitDays, decimal transferDays)
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 25m,
            used = 0m,
            planned = 0m,
            carryoverIn = 0m,
            annualQuota = 25m,
            carryoverMax = 5m,
            resetMonth = 9,
            okVersion = "OK24",
            agreementCode = "HK",
            transferAgreementDays = transferDays,
            isFeriehindret = false,
            terminationDate = (string?)null,
            crystallizationBasis = (string?)null,
            crystallizedDays = 0m,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days, version)
            VALUES
                (@e, 'VACATION', @y, 1, 'PENDING_REVIEW', 'YEAR_END', @snapshot::jsonb, @transfer, 0, @forfeit, 1)
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        cmd.Parameters.AddWithValue("transfer", transferDays);
        cmd.Parameters.AddWithValue("forfeit", forfeitDays);
        await cmd.ExecuteNonQueryAsync();
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

    /// <summary>Closes the open VACATION row of a key at <paramref name="effectiveTo"/> (the soft-delete shape).</summary>
    private async Task CloseOpenRowAsync(string agreementCode, string okVersion, DateOnly effectiveTo)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE entitlement_configs SET effective_to = @to
            WHERE entitlement_type = 'VACATION' AND agreement_code = @a AND ok_version = @ok AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("to", effectiveTo);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private async Task<bool> HasOpenRowAsync(string agreementCode, string okVersion)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM entitlement_configs
            WHERE entitlement_type = 'VACATION' AND agreement_code = @a AND ok_version = @ok AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task<decimal?> DatedQuotaOrNullAsync(string agreementCode, string okVersion, DateOnly asOf)
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
        var r = await cmd.ExecuteScalarAsync();
        return r is null or DBNull ? null : Convert.ToDecimal(r);
    }

    /// <summary>The VACATION quota of (agreement, OK version) in force on <paramref name="asOf"/>; null =
    /// the live (open) row.</summary>
    private async Task<decimal> DatedQuotaAsync(string agreementCode, string okVersion, DateOnly? asOf)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT annual_quota FROM entitlement_configs
            WHERE entitlement_type = 'VACATION' AND agreement_code = @a AND ok_version = @ok
              AND ((@d::date IS NULL AND effective_to IS NULL)
                   OR (effective_from <= @d::date AND (effective_to IS NULL OR effective_to > @d::date)))
            """, conn);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.Add(new NpgsqlParameter("d", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)asOf ?? DBNull.Value });
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync());
    }

    private async Task<(decimal TotalQuota, decimal Used, decimal CarryoverIn)?> ReadBalanceAsync(string emp, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT total_quota, used, carryover_in FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = 'VACATION' AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return (reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2));
    }

    private async Task<decimal> ReadSnapshotAnnualQuotaAsync(string emp, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT (snapshot->>'annualQuota')::numeric FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = 'VACATION' AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("y", year);
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync());
    }
}
