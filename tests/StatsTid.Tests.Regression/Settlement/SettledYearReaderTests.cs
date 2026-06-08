using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Balance; // FixedTimeProvider
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S68 / TASK-6808 (ADR-033 D6 clarification / TASK-6807) — Docker-gated tests for the settled-year
/// readers in <see cref="StatsTid.Backend.Api.Endpoints.BalanceEndpoints"/> (prompt scenario 11):
/// a SETTLED entitlement-year reads <c>remaining = 0</c> plus the recorded disposition; a
/// PENDING_REVIEW year shows the §34 remainder pending (NOT 0); an unsettled year is unchanged; the
/// monthly <c>saldo</c> array is NOT retroactively zeroed by settlement.
///
/// <para>Exercises both reader surfaces:
/// <list type="bullet">
///   <item><c>GET /api/balance/{id}/summary?year=&amp;month=</c> — the per-category
///   <c>remaining</c> + <c>settlement</c> object (the CURRENT-year tile branch).</item>
///   <item><c>GET /api/balance/{id}/year-overview?year=</c> — the closed-ferieår <c>expiring</c> +
///   <c>settlement</c> disposition AND the untouched per-month <c>saldo</c>.</item>
/// </list>
/// VACATION reset_month 9 ⇒ for <c>/summary?year=2026&amp;month=3</c> the entitlement_year is 2025
/// (months &lt; Sep map to year−1); we seed the settlement on entitlement_year 2025 to hit the
/// branch. The year-overview is driven with a fixed <see cref="TimeProvider"/> for determinism.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettledYearReaderTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";

    // Fixed today for the year-overview determinism (mid-2026, OK26 era), mirrors YearOverviewTests.
    private static readonly DateOnly FixedToday = new(2026, 6, 15);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (VACATION config quota 25 / carryover_max 5)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // /summary — SETTLED ⇒ remaining 0 + full disposition; forfeitPending false.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A SETTLED VACATION year (entitlement_year 2025) read via <c>/summary?year=2026&amp;month=3</c>
    /// (maps to entitlement_year 2025) shows <c>remaining = 0</c> and the recorded
    /// §21/§24/§34 disposition (transfer/payout/forfeit), with <c>forfeitPending = false</c>. used is
    /// NOT mutated by settlement — only the displayed remaining changes.
    /// </summary>
    [Fact]
    public async Task Summary_SettledYear_RemainingZero_ShowsFullDisposition()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, 2025, state: "SETTLED",
            transfer: 5m, payout: 0m, forfeit: 20m);

        var client = ClientWith(EmployeeToken(employeeId, OrgId));
        var vacation = await GetSummaryCategoryAsync(client, employeeId, year: 2026, month: 3, "VACATION");

        Assert.Equal(0m, vacation.GetProperty("remaining").GetDecimal());
        var settlement = vacation.GetProperty("settlement");
        Assert.Equal(JsonValueKind.Object, settlement.ValueKind);
        Assert.Equal("SETTLED", settlement.GetProperty("state").GetString());
        Assert.Equal(5m, settlement.GetProperty("transferDays").GetDecimal());
        Assert.Equal(0m, settlement.GetProperty("payoutDays").GetDecimal());
        Assert.Equal(20m, settlement.GetProperty("forfeitDays").GetDecimal());
        Assert.False(settlement.GetProperty("forfeitPending").GetBoolean());
    }

    // ════════════════════════════════════════════════════════════════════════
    // /summary — PENDING_REVIEW ⇒ remaining = §34 remainder (NOT 0); forfeitPending true.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A PENDING_REVIEW VACATION year shows the unresolved §34 <c>forfeit_days</c> remainder as the
    /// displayed <c>remaining</c> (NOT 0 — Codex W), with <c>forfeitPending = true</c>. The §21/§24
    /// buckets are still surfaced in the disposition.
    /// </summary>
    [Fact]
    public async Task Summary_PendingReviewYear_ShowsForfeitRemainderPending_NotZero()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, 2025, state: "PENDING_REVIEW",
            transfer: 0m, payout: 5m, forfeit: 20m);

        var client = ClientWith(EmployeeToken(employeeId, OrgId));
        var vacation = await GetSummaryCategoryAsync(client, employeeId, year: 2026, month: 3, "VACATION");

        Assert.Equal(20m, vacation.GetProperty("remaining").GetDecimal()); // the §34 remainder, NOT 0
        var settlement = vacation.GetProperty("settlement");
        Assert.Equal("PENDING_REVIEW", settlement.GetProperty("state").GetString());
        Assert.True(settlement.GetProperty("forfeitPending").GetBoolean());
        Assert.Equal(20m, settlement.GetProperty("forfeitDays").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // /summary — unsettled ⇒ unchanged (settlement null; remaining is the live figure).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An UNSETTLED VACATION year is byte-identical to prior behavior: <c>settlement</c> is null and
    /// <c>remaining</c> is the live earned-based figure (NOT forced to 0). Pins the no-new-behavior
    /// path for any year without a settlement row.
    /// </summary>
    [Fact]
    public async Task Summary_UnsettledYear_Unchanged_SettlementNull()
    {
        var employeeId = await SeedEmployeeAsync();
        // No settlement row seeded.

        var client = ClientWith(EmployeeToken(employeeId, OrgId));
        var vacation = await GetSummaryCategoryAsync(client, employeeId, year: 2026, month: 3, "VACATION");

        Assert.Equal(JsonValueKind.Null, vacation.GetProperty("settlement").ValueKind);
        // Live remaining is positive (a fully-accrued-by-March ferieår 2025 with no consumption).
        Assert.True(vacation.GetProperty("remaining").GetDecimal() > 0m);
    }

    // ════════════════════════════════════════════════════════════════════════
    // year-overview — closed-ferieår disposition + the monthly saldo NOT zeroed.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// In the year-overview for selected year 2026, the CLOSED boundary ferieår is 2025 (Sep 2025 ..
    /// Aug 2026). A SETTLED settlement on entitlement_year 2025 makes the VACATION
    /// <c>settlement</c> disposition appear with the recorded buckets, the <c>expiring</c> figure
    /// pins to the recorded §34 <c>forfeit_days</c>, AND the per-month <c>saldo</c> array is left
    /// UNTOUCHED (settlement does not retroactively zero the ferieår's monthly history — ADR-033 D6).
    /// </summary>
    [Fact]
    public async Task YearOverview_SettledClosedFerieaar_ShowsDisposition_SaldoNotZeroed()
    {
        var employeeId = await SeedEmployeeAsync();
        // Closed boundary ferieår for selected year 2026 = ferieår 2025. Seed a SETTLED row there.
        await SeedSettlementRowAsync(employeeId, 2025, state: "SETTLED",
            transfer: 5m, payout: 0m, forfeit: 20m);

        var client = MakeFixedTodayClient(EmployeeToken(employeeId, OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2026);
        var vacation = GetCategory(body, "VACATION");

        // The recorded disposition appears and expiring == the recorded §34 forfeit_days (20).
        var settlement = vacation.GetProperty("settlement");
        Assert.Equal(JsonValueKind.Object, settlement.ValueKind);
        Assert.Equal("SETTLED", settlement.GetProperty("state").GetString());
        Assert.Equal(20m, settlement.GetProperty("forfeitDays").GetDecimal());
        Assert.Equal(20m, vacation.GetProperty("expiring").GetDecimal()); // pinned to forfeit_days

        // The per-month saldo is NOT all-zero — settlement does not zero the ferieår monthly history.
        var saldo = vacation.GetProperty("saldo").EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.Null ? (decimal?)null : e.GetDecimal())
            .ToList();
        Assert.Equal(12, saldo.Count);
        Assert.Contains(saldo, s => s is > 0m); // at least one month carries a positive accrual saldo
    }

    /// <summary>
    /// A PENDING_REVIEW closed ferieår shows <c>expiring</c> pinned to the unresolved §34
    /// <c>forfeit_days</c> with <c>forfeitPending = true</c> in the disposition (the §34 remainder is
    /// still flagged, not 0).
    /// </summary>
    [Fact]
    public async Task YearOverview_PendingReviewClosedFerieaar_ExpiringIsForfeitPending()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, 2025, state: "PENDING_REVIEW",
            transfer: 0m, payout: 5m, forfeit: 20m);

        var client = MakeFixedTodayClient(EmployeeToken(employeeId, OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2026);
        var vacation = GetCategory(body, "VACATION");

        Assert.Equal(20m, vacation.GetProperty("expiring").GetDecimal());
        var settlement = vacation.GetProperty("settlement");
        Assert.Equal("PENDING_REVIEW", settlement.GetProperty("state").GetString());
        Assert.True(settlement.GetProperty("forfeitPending").GetBoolean());
    }

    // ─────────────────────────────── HTTP helpers ───────────────────────────────

    private static async Task<JsonElement> GetSummaryCategoryAsync(
        HttpClient client, string employeeId, int year, int month, string type)
    {
        var rsp = await client.GetAsync($"/api/balance/{employeeId}/summary?year={year}&month={month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        // /summary's per-category array is `entitlements`, each item keyed by `type` (BalanceEndpoints).
        return body.GetProperty("entitlements").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == type);
    }

    private static async Task<JsonElement> GetYearOverviewAsync(HttpClient client, string employeeId, int year)
    {
        var rsp = await client.GetAsync($"/api/balance/{employeeId}/year-overview?year={year}");
        rsp.EnsureSuccessStatusCode();
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement GetCategory(JsonElement body, string type) =>
        body.GetProperty("categories").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == type);

    private HttpClient ClientWith(string bearer)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private HttpClient MakeFixedTodayClient(string bearer)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedToday));
            });
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private static string EmployeeToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.Employee,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s68_reader_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    private async Task SeedSettlementRowAsync(
        string employeeId, int year, string state, decimal transfer, decimal payout, decimal forfeit)
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 25m, used = 0m, planned = 0m, carryoverIn = 0m,
            annualQuota = 25m, carryoverMax = 5m, resetMonth = 9, okVersion = "OK24",
            transferAgreementDays = transfer, isFeriehindret = false,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days, version)
            VALUES
                (@e, @t, @y, 1, @state, 'YEAR_END', @snapshot::jsonb, @transfer, @payout, @forfeit, 1)
            ON CONFLICT (employee_id, entitlement_type, entitlement_year, sequence)
                DO UPDATE SET settlement_state = EXCLUDED.settlement_state,
                              transfer_days = EXCLUDED.transfer_days,
                              payout_days = EXCLUDED.payout_days,
                              forfeit_days = EXCLUDED.forfeit_days
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        cmd.Parameters.AddWithValue("transfer", transfer);
        cmd.Parameters.AddWithValue("payout", payout);
        cmd.Parameters.AddWithValue("forfeit", forfeit);
        await cmd.ExecuteNonQueryAsync();
    }
}
