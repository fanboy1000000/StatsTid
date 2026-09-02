using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Http;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Compliance;

/// <summary>
/// S137 / TASK-13703 (ADR-040 D7 + D10, the Codex-B3 absorption) — Docker-gated HTTP pins for the
/// employment-window behaviour of <c>GET /api/compliance/{employeeId}/period</c>.
///
/// <para>
/// <b>What changed and why (plain language).</b> Since S136 a new employee's profile history
/// starts at the HIRE date. The compliance check used to resolve the profile at the 1st of the
/// month, so for a mid-month hire it found no profile and returned a 500 — and for a month entirely
/// before the hire it would have run a meaningless check. Now the handler asks "was this person
/// employed on any day of this month?" FIRST (server-side, via <see cref="IEmploymentWindowResolver"/>),
/// returns an empty "nothing to check" result when the answer is no, and otherwise resolves the
/// profile at the FIRST employed day. The check's geometry stays whole-month; the wire shape is
/// unchanged; employment dates never appear in the response.
/// </para>
///
/// <para>
/// <b>How the pins observe the internals.</b> The derived WAF host swaps in RECORDING decorators
/// around the REAL <see cref="EmploymentProfileResolver"/> and <see cref="EmploymentWindowResolver"/>
/// (so every as-of date they are asked for is captured) and replaces the named rule-engine client's
/// PRIMARY handler with a recorder that serves a canned, distinctive result (the
/// <c>S120ComplianceSpecRuntimeTests</c> idiom — real pipeline, no socket). The canned body carries
/// ONE warning, so "passed through verbatim" and "not called at all" are distinguishable.
/// </para>
///
/// <para>
/// <b>Bonus QUAL-147 pin.</b> The captured rule-engine request body exposes <c>profile.okVersion</c>
/// — for a hire on the 2026-04-01 OK boundary it must read <c>OK26</c> although the live
/// <c>users.ok_version</c> row says <c>OK24</c> (the resolver now resolves OK from the date).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ComplianceEmploymentWindowTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string CheckCompliancePath = "/api/rules/check-compliance";
    private const string CannedRuleId = "REST_PERIOD_CHECK";
    private const string CannedWarningMessage = "canned-rule-engine-warning";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _stubbed = null!;

    private readonly RecordingProfileResolver _profileRecorder = new();
    private readonly RecordingWindowResolver _windowRecorder = new();
    private readonly RecordingRuleEngineHandler _ruleEngineRecorder = new();

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot the seeders once

        // The ONE derived host every test uses — built BEFORE per-test seeding so its boot-time
        // seeders cannot backfill anything for the hire-dated employees the tests create (both
        // seeders only fill users lacking a LIVE row, but building first removes the question).
        _stubbed = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmploymentProfileResolver>();
                services.AddSingleton<IEmploymentProfileResolver>(sp =>
                {
                    _profileRecorder.Inner = new EmploymentProfileResolver(
                        sp.GetRequiredService<DbConnectionFactory>(),
                        sp.GetRequiredService<UserAgreementCodeRepository>());
                    return _profileRecorder;
                });

                services.RemoveAll<IEmploymentWindowResolver>();
                services.AddSingleton<IEmploymentWindowResolver>(sp =>
                {
                    _windowRecorder.Inner = sp.GetRequiredService<EmploymentWindowResolver>();
                    return _windowRecorder;
                });

                services.Configure<HttpClientFactoryOptions>(RuleEngineClient.Name, o =>
                {
                    // One shared recorder for the whole class; never rotate it out mid-test.
                    o.HandlerLifetime = Timeout.InfiniteTimeSpan;
                    o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _ruleEngineRecorder);
                });
            }));
        _ = _stubbed.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _stubbed?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // (i) A month entirely BEFORE the hire — nothing to check.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hire 2026-05-10, month April 2026: 200 with the EXISTING shape, zero violations/warnings,
    /// <c>success=true</c>, the rule engine's RuleId echoed — and NEITHER the profile resolver NOR
    /// the rule engine is called. Pre-S137 this resolved the profile at 1 Apr, found no covering
    /// row (the row starts at hire) and 500'd.
    /// </summary>
    [Fact]
    public async Task PreHireMonth_Returns200_ZeroViolations_NoProfileResolve_NoRuleEngineCall()
    {
        var hire = new DateOnly(2026, 5, 10);
        var employeeId = await SeedEmployeeAsync(hire, end: null, profileFrom: hire);
        ResetRecorders();

        var rsp = await Client(employeeId).GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=4");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CannedRuleId, body.GetProperty("ruleId").GetString());
        Assert.Equal(employeeId, body.GetProperty("employeeId").GetString());
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Empty(body.GetProperty("violations").EnumerateArray());
        Assert.Empty(body.GetProperty("warnings").EnumerateArray());
        // The exact key set is unchanged — no new wire field (e.g. no employment date) leaked in.
        Assert.Equal(
            new HashSet<string> { "ruleId", "employeeId", "success", "violations", "warnings" },
            body.EnumerateObject().Select(p => p.Name).ToHashSet());

        Assert.Empty(_profileRecorder.AsOfDates);
        Assert.Empty(_ruleEngineRecorder.Requests(CheckCompliancePath));
        // The window WAS consulted (once, for the month range) — that is what decided "nothing".
        var windowCall = Assert.Single(_windowRecorder.RangeCalls);
        Assert.Equal((employeeId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)), windowCall);
    }

    // ════════════════════════════════════════════════════════════════════════
    // (ii) A mid-month hire — resolve the profile at the hire date, keep whole-month geometry.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hire 2026-03-15 (profile + agreement rows start at hire), month March 2026: 200 (pre-S137 →
    /// 500: the resolver at 2026-03-01 found no covering row → <c>EmployeeProfileNotFoundException</c>).
    /// The profile is resolved EXACTLY once, at the hire date; the rule engine is called EXACTLY once
    /// with the UNCHANGED whole-month period (1..31 Mar).
    /// </summary>
    [Fact]
    public async Task MidMonthHire_ResolvesProfileAtHireDate_Returns200_WholeMonthGeometryUnchanged()
    {
        var hire = new DateOnly(2026, 3, 15);
        var employeeId = await SeedEmployeeAsync(hire, end: null, profileFrom: hire);
        ResetRecorders();

        var rsp = await Client(employeeId).GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=3");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(hire, Assert.Single(_profileRecorder.AsOfDates));

        var call = Assert.Single(_ruleEngineRecorder.Requests(CheckCompliancePath));
        using var req = JsonDocument.Parse(call.Body);
        Assert.Equal("2026-03-01", req.RootElement.GetProperty("periodStart").GetString());
        Assert.Equal("2026-03-31", req.RootElement.GetProperty("periodEnd").GetString());
        // D7: the wire DTO carries NO employment dates.
        var profileKeys = req.RootElement.GetProperty("profile").EnumerateObject().Select(p => p.Name).ToList();
        Assert.DoesNotContain(profileKeys, k => k.Contains("employment", StringComparison.OrdinalIgnoreCase)
                                             && k.Contains("date", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A hire ON the 2026-04-01 OK24→OK26 boundary for a live-OK24 user: the profile shipped to the
    /// rule engine carries <c>okVersion = OK26</c> — resolved from the as-of DATE (the hire day),
    /// not from <c>users.ok_version</c> (QUAL-147 closed inside the resolver, observed end-to-end
    /// through compliance).
    /// </summary>
    [Fact]
    public async Task OkBoundaryHire_ProfileOkVersionIsDateResolved_NotTheLiveColumn()
    {
        var hire = new DateOnly(2026, 4, 1);
        var employeeId = await SeedEmployeeAsync(hire, end: null, profileFrom: hire, okVersion: "OK24");
        ResetRecorders();

        var rsp = await Client(employeeId).GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=4");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(hire, Assert.Single(_profileRecorder.AsOfDates));
        var call = Assert.Single(_ruleEngineRecorder.Requests(CheckCompliancePath));
        using var req = JsonDocument.Parse(call.Body);
        Assert.Equal("OK26", req.RootElement.GetProperty("profile").GetProperty("okVersion").GetString());
        Assert.Equal("OK24", await ReadUsersOkVersionAsync(employeeId)); // the live column really is OK24
    }

    // ════════════════════════════════════════════════════════════════════════
    // (iii) A windowless employee — behaviour unchanged.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// No employment dates (the pre-S136 population): the profile is resolved at monthStart exactly
    /// as before, the rule engine is called once, and its result is passed through VERBATIM (the
    /// canned warning survives) — the pre-S137 contract, byte-for-byte.
    /// </summary>
    [Fact]
    public async Task WindowlessEmployee_ResolvesAtMonthStart_PassesRuleEngineResultThrough()
    {
        var employeeId = await SeedEmployeeAsync(start: null, end: null, profileFrom: null);
        ResetRecorders();

        var rsp = await Client(employeeId).GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=3");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(new DateOnly(2026, 3, 1), Assert.Single(_profileRecorder.AsOfDates));
        Assert.Single(_ruleEngineRecorder.Requests(CheckCompliancePath));

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CannedRuleId, body.GetProperty("ruleId").GetString());
        var warning = Assert.Single(body.GetProperty("warnings").EnumerateArray());
        Assert.Equal(CannedWarningMessage, warning.GetProperty("message").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════
    // Leaver — the end side of the window.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Last employed day 2026-03-15: March still has an employed first day (1 Mar) so the check
    /// runs as before; April has NO employed day ⇒ the empty result, no profile resolve, no
    /// rule-engine call.
    /// </summary>
    [Fact]
    public async Task Leaver_LeaveMonthChecks_PostLeaveMonthIsEmpty()
    {
        var end = new DateOnly(2026, 3, 15);
        var employeeId = await SeedEmployeeAsync(start: null, end: end, profileFrom: null);
        var client = Client(employeeId);

        ResetRecorders();
        var march = await client.GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=3");
        Assert.Equal(HttpStatusCode.OK, march.StatusCode);
        Assert.Equal(new DateOnly(2026, 3, 1), Assert.Single(_profileRecorder.AsOfDates));
        Assert.Single(_ruleEngineRecorder.Requests(CheckCompliancePath));

        ResetRecorders();
        var april = await client.GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=4");
        Assert.Equal(HttpStatusCode.OK, april.StatusCode);
        var body = await april.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Empty(body.GetProperty("violations").EnumerateArray());
        Assert.Empty(body.GetProperty("warnings").EnumerateArray());
        Assert.Empty(_profileRecorder.AsOfDates);
        Assert.Empty(_ruleEngineRecorder.Requests(CheckCompliancePath));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Access control still runs BEFORE the window read (checklist pin).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A foreign employee's read is refused with 403 BEFORE any window / profile / rule-engine read
    /// happens — the S137 window read sits after the existing access checks, not before them.
    /// </summary>
    [Fact]
    public async Task ForeignEmployee_403_BeforeAnyWindowOrProfileRead()
    {
        var target = await SeedEmployeeAsync(new DateOnly(2026, 3, 15), end: null, profileFrom: new DateOnly(2026, 3, 15));
        var alien = await SeedEmployeeAsync(start: null, end: null, profileFrom: null);
        ResetRecorders();

        var rsp = await Client(alien).GetAsync($"/api/compliance/{target}/period?year=2026&month=3");

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Empty(_windowRecorder.RangeCalls);
        Assert.Empty(_profileRecorder.AsOfDates);
        Assert.Empty(_ruleEngineRecorder.Requests(CheckCompliancePath));
    }

    // ─────────────────────────────── recorders ───────────────────────────────

    private void ResetRecorders()
    {
        _profileRecorder.Reset();
        _windowRecorder.Reset();
        _ruleEngineRecorder.Reset();
    }

    /// <summary>Decorates the REAL profile resolver; records every as-of date asked for.</summary>
    private sealed class RecordingProfileResolver : IEmploymentProfileResolver
    {
        private readonly List<DateOnly> _asOfDates = new();
        public IEmploymentProfileResolver Inner { get; set; } = null!;
        public IReadOnlyList<DateOnly> AsOfDates { get { lock (_asOfDates) return _asOfDates.ToList(); } }
        public void Reset() { lock (_asOfDates) _asOfDates.Clear(); }

        public Task<EmploymentProfile?> GetByEmployeeIdAtAsync(string employeeId, DateOnly asOfDate, CancellationToken ct = default)
        {
            lock (_asOfDates) _asOfDates.Add(asOfDate);
            return Inner.GetByEmployeeIdAtAsync(employeeId, asOfDate, ct);
        }
    }

    /// <summary>Decorates the REAL window resolver; records every range query.</summary>
    private sealed class RecordingWindowResolver : IEmploymentWindowResolver
    {
        private readonly List<(string EmployeeId, DateOnly From, DateOnly To)> _rangeCalls = new();
        public IEmploymentWindowResolver Inner { get; set; } = null!;
        public IReadOnlyList<(string EmployeeId, DateOnly From, DateOnly To)> RangeCalls
        { get { lock (_rangeCalls) return _rangeCalls.ToList(); } }
        public void Reset() { lock (_rangeCalls) _rangeCalls.Clear(); }

        public Task<EmploymentWindowStatus> GetStatusAsync(string employeeId, DateOnly date, CancellationToken ct = default)
            => Inner.GetStatusAsync(employeeId, date, ct);

        public Task<IReadOnlyList<EmploymentWindow>> GetWindowsAsync(string employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            lock (_rangeCalls) _rangeCalls.Add((employeeId, from, to));
            return Inner.GetWindowsAsync(employeeId, from, to, ct);
        }
    }

    /// <summary>
    /// The named rule-engine client's PRIMARY handler: records (path, body) and serves a canned
    /// 200 ComplianceCheckResult with ONE warning (enum values as ints — the Backend deserializes
    /// with camelCase + default enum handling).
    /// </summary>
    private sealed class RecordingRuleEngineHandler : HttpMessageHandler
    {
        private readonly List<(string Path, string Body)> _requests = new();

        public IReadOnlyList<(string Path, string Body)> Requests(string pathSuffix)
        {
            lock (_requests)
                return _requests.Where(r => r.Path.EndsWith(pathSuffix, StringComparison.Ordinal)).ToList();
        }

        public void Reset() { lock (_requests) _requests.Clear(); }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_requests) _requests.Add((request.RequestUri?.AbsolutePath ?? string.Empty, body));

            var canned =
                "{\"ruleId\":\"" + CannedRuleId + "\",\"employeeId\":\"stub\",\"success\":true," +
                "\"violations\":[]," +
                "\"warnings\":[{\"violationType\":2,\"date\":\"2026-03-05\",\"actualValue\":13.5," +
                "\"thresholdValue\":13.0,\"severity\":0,\"isVoluntaryExempt\":false," +
                "\"message\":\"" + CannedWarningMessage + "\"}]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(canned, Encoding.UTF8, "application/json"),
            };
        }
    }

    // ─────────────────────────────── seeding / clients ───────────────────────────────

    /// <summary>
    /// Seeds the resolver triple (users + employee_profiles + user_agreement_codes) with the
    /// profile/agreement rows starting at <paramref name="profileFrom"/> (null ⇒ 0001-01-01, the
    /// pre-S136 backfill anchor), then stamps the employment window on the users row.
    /// </summary>
    private async Task<string> SeedEmployeeAsync(DateOnly? start, DateOnly? end, DateOnly? profileFrom, string okVersion = "OK24")
    {
        var employeeId = "emp_s137_cw_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, OrgId, "AC", okVersion, effectiveFrom: profileFrom);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_start_date = @s, employment_end_date = @e WHERE user_id = @id", conn);
        cmd.Parameters.Add(new NpgsqlParameter("s", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)start ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("e", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)end ?? DBNull.Value });
        cmd.Parameters.AddWithValue("id", employeeId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        return employeeId;
    }

    private async Task<string> ReadUsersOkVersionAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT ok_version FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private HttpClient Client(string employeeId)
    {
        var client = _stubbed.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId));
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
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
    }
}
