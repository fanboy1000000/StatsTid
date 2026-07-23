using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using StatsTid.Backend.Api.Http;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S120 / TASK-12002 — the per-route spec≡runtime gate for the COMPLIANCE family drained in
/// retrofit Pass 7 (TASK-12000): the period + governance passthroughs of the NAMED SharedKernel
/// <c>ComplianceCheckResult</c> (ONE model, TWO ops) and the compensatory-rest bare array.
///
/// <para><b>OWNER RULING #3 (dead-branch class, the S118-ruling-#1 lineage — ONE ruling, TWO
/// ops):</b> a literal-null 2xx body from the rule engine now maps to <b>502 upstream-invalid</b>
/// at BOTH sites, making the declared 200 STRUCTURALLY the full result. Both the 502 guard and
/// the 200 path are exercised via the ESTABLISHED stub idiom
/// (<c>RuleEngineAuthForwardingTests.cs:33-39</c>): the WAF keeps the REAL named-client
/// registration and replaces ONLY the named client's PRIMARY handler via
/// <see cref="HttpClientFactoryOptions"/> — no factory/Hosting change; no real socket. The 200
/// path necessarily ALSO runs against the stub (no rule engine boots in this harness) — honest
/// because the handler ROUND-TRIPS the stub body through the real CLR
/// <see cref="ComplianceCheckResult"/> before re-serializing.</para>
///
/// <para><b>The INTEGER-enum wire (TASK-12000's DECLARED CHECKSUM DISCREPANCY — the code
/// wins):</b> <c>violationType</c>/<c>severity</c> serialize as INTEGERS (no
/// <c>JsonStringEnumConverter</c> in the HTTP path; the spec truthfully emits
/// <c>type: integer</c>). The value-set fidelity is asserted BOTH ways per Step-0b Reviewer N1:
/// spec-vs-CLR-enum (the declared sets equal the CLR enums' full value sets) AND via a stub
/// body enumerating ALL 6 <c>violationType</c> + BOTH <c>severity</c> values through the real
/// CLR round-trip — never inferred from a canned single-violation body.</para>
///
/// <para><b>Seed discipline (FAIL-002):</b> a FRESH testcontainer per test; org
/// <c>S120CMP1</c>, employees <c>s120c_*</c> (via the canonical <see cref="RegressionSeed"/> —
/// the period op's profile resolve is fail-loud). compensatory_rest rows are INPUT data.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S120ComplianceSpecRuntimeTests : IAsyncLifetime
{
    private const string Org = "S120CMP1";

    /// <summary>The EXACT 5 members of the NAMED <c>ComplianceCheckResult</c>.</summary>
    private static readonly string[] ResultKeys = { "ruleId", "employeeId", "success", "violations", "warnings" };

    /// <summary>The EXACT 7 members of a <c>ComplianceViolation</c> row.</summary>
    private static readonly string[] ViolationKeys =
        { "violationType", "date", "actualValue", "thresholdValue", "severity", "isVoluntaryExempt", "message" };

    /// <summary>The EXACT 7 members of a <c>CompensatoryRestItem</c> row.</summary>
    private static readonly string[] CompensatoryRestKeys =
        { "id", "employeeId", "sourceDate", "compensatoryDate", "hours", "status", "createdAt" };

    /// <summary>A stub rule-engine body carrying ALL SIX <c>violationType</c> CLR values (0..5)
    /// and BOTH <c>severity</c> values — the full-value-set body that round-trips the real CLR
    /// contract (never a canned single-violation body; Step-0b Reviewer N1).</summary>
    private const string AllValuesBody =
        """
        {"ruleId":"S120_STUB_RULE","employeeId":"s120c-stub-echo","success":false,
         "violations":[
           {"violationType":0,"date":"2026-03-02","actualValue":9.5,"thresholdValue":11.0,"severity":1,"isVoluntaryExempt":false,"message":"S120 daglig hvile"},
           {"violationType":1,"date":"2026-03-03","actualValue":20.0,"thresholdValue":24.0,"severity":1,"isVoluntaryExempt":false,"message":"S120 ugentlig hvile"},
           {"violationType":2,"date":"2026-03-04","actualValue":13.5,"thresholdValue":13.0,"severity":0,"isVoluntaryExempt":true,"message":"S120 maks daglige timer"},
           {"violationType":3,"date":"2026-03-05","actualValue":49.0,"thresholdValue":48.0,"severity":1,"isVoluntaryExempt":false,"message":"S120 48-timers reglen"},
           {"violationType":4,"date":"2026-03-06","actualValue":12.0,"thresholdValue":10.0,"severity":0,"isVoluntaryExempt":false,"message":"S120 overarbejde overskredet"},
           {"violationType":5,"date":"2026-03-09","actualValue":5.0,"thresholdValue":0.0,"severity":1,"isVoluntaryExempt":false,"message":"S120 ikke-godkendt overarbejde"}],
         "warnings":[
           {"violationType":3,"date":"2026-03-10","actualValue":46.0,"thresholdValue":48.0,"severity":0,"isVoluntaryExempt":false,"message":"S120 advarsel"}]}
        """;

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 1 — GET /api/compliance/{employeeId}/period — the structurally-full 200.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The RULING-#3 structurally-full 200 + the full ENUM value-set proof: the stub
    /// serves all 6 <c>violationType</c> + both <c>severity</c> values; the endpoint round-trips
    /// them through the real CLR <c>ComplianceCheckResult</c>; the matcher walks every row
    /// (INTEGER enum-fidelity live on every value). Pins the DECLARED DISCREPANCY — the enums
    /// are NUMBERS on the wire — and asserts the spec's declared sets equal the CLR enums'
    /// full value sets (spec-vs-CLR, both dimensions of Reviewer N1).</summary>
    [Fact]
    public async Task CompliancePeriod_Get200_StructurallyFull_AllSixIntegerEnumValues_SpecVsClrSets()
    {
        var emp = await SeedEmployeeAsync("full1");
        using var app = StubbedHost(AllValuesBody);
        using var employee = S120ContractAssert.EmployeeClient(app, emp, Org);

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(
                HttpMethod.Get, $"/api/compliance/{emp}/period?year=2026&month=3"),
            "/api/compliance/{employeeId}/period", "get");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, ResultKeys, "compliance period result");
        Assert.Equal("S120_STUB_RULE", root.GetProperty("ruleId").GetString()); // CLR round-trip passthrough
        Assert.Equal("s120c-stub-echo", root.GetProperty("employeeId").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());

        var violations = root.GetProperty("violations").EnumerateArray().ToList();
        Assert.Equal(6, violations.Count);
        foreach (var violation in violations)
        {
            S118ContractAssert.AssertExactKeySet(violation, ViolationKeys, "compliance violation row");
            // The DECLARED DISCREPANCY pinned: INTEGERS on the wire, never strings.
            Assert.Equal(JsonValueKind.Number, violation.GetProperty("violationType").ValueKind);
            Assert.Equal(JsonValueKind.Number, violation.GetProperty("severity").ValueKind);
        }
        // ALL SIX violationType values through the real CLR contract (N1: never a canned single).
        var servedTypes = violations.Select(v => v.GetProperty("violationType").GetInt32()).ToHashSet();
        Assert.Equal(new HashSet<int> { 0, 1, 2, 3, 4, 5 }, servedTypes);
        var servedSeverities = violations.Select(v => v.GetProperty("severity").GetInt32()).ToHashSet();
        Assert.Equal(new HashSet<int> { 0, 1 }, servedSeverities);

        // Spec-vs-CLR value-set fidelity: the committed spec's declared integer sets equal the
        // CLR enums' FULL value sets (6 + 2) — the single source both ops' schemas $ref.
        var specViolationTypes = SchemaEnumInts("StatsTid.SharedKernel.Models.ComplianceViolationType");
        Assert.Equal(
            Enum.GetValues<ComplianceViolationType>().Select(v => (int)v).ToHashSet(),
            specViolationTypes);
        Assert.Equal(6, specViolationTypes.Count);
        var specSeverities = SchemaEnumInts("StatsTid.SharedKernel.Models.ComplianceSeverity");
        Assert.Equal(
            Enum.GetValues<ComplianceSeverity>().Select(v => (int)v).ToHashSet(),
            specSeverities);
        Assert.Equal(2, specSeverities.Count);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  RULING #3 — the null→502 guard, BOTH ops (ONE ruling, TWO sites).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary><b>The RULING-#3 pins:</b> a stub returning a LITERAL-NULL 2xx body (the proven
    /// defensive dead branch — <c>ReadFromJsonAsync</c> yields null) maps to <b>502</b> at BOTH
    /// sites: the compliance period op AND the overtime governance op. NOT left untested — the
    /// guard is exercised end-to-end through the real handlers via the named-client
    /// primary-handler replacement.</summary>
    [Fact]
    public async Task Ruling3_LiteralNullUpstreamBody_Maps502_BothOps()
    {
        var emp = await SeedEmployeeAsync("null1");
        using var app = StubbedHost("null"); // the literal-null JSON body
        using var employee = S120ContractAssert.EmployeeClient(app, emp, Org);

        using (var period = await employee.GetAsync($"/api/compliance/{emp}/period?year=2026&month=3"))
        {
            Assert.Equal(502, (int)period.StatusCode); // upstream-invalid, never a null-bodied 200
            var body = await period.Content.ReadAsStringAsync();
            Assert.Contains("Invalid compliance check response", body, StringComparison.Ordinal);
        }

        using (var governance = await employee.GetAsync(
            $"/api/overtime/{emp}/governance?periodStart=2026-03-01&periodEnd=2026-03-31&overtimeHours=5"))
        {
            Assert.Equal(502, (int)governance.StatusCode);
            var body = await governance.Content.ReadAsStringAsync();
            Assert.Contains("Invalid overtime governance response", body, StringComparison.Ordinal);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 2 — GET /api/overtime/{employeeId}/governance — the same model, second site.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The governance passthrough of the SAME named model (the sibling site of ruling
    /// #3): the all-values stub body round-trips the real CLR contract and the matcher walks the
    /// full result — integer enum fidelity live on all 6 + 2 values at THIS site too, at the
    /// Employee floor.</summary>
    [Fact]
    public async Task OvertimeGovernance_Get200_StructurallyFull_SameClrModelRoundTrip()
    {
        var emp = await SeedEmployeeAsync("gov1");
        using var app = StubbedHost(AllValuesBody);
        using var employee = S120ContractAssert.EmployeeClient(app, emp, Org);

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get,
                $"/api/overtime/{emp}/governance?periodStart=2026-03-01&periodEnd=2026-03-31&overtimeHours=5"),
            "/api/overtime/{employeeId}/governance", "get");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, ResultKeys, "overtime governance result");
        Assert.Equal("S120_STUB_RULE", root.GetProperty("ruleId").GetString());
        var violations = root.GetProperty("violations").EnumerateArray().ToList();
        Assert.Equal(6, violations.Count);
        Assert.Equal(
            new HashSet<int> { 0, 1, 2, 3, 4, 5 },
            violations.Select(v => v.GetProperty("violationType").GetInt32()).ToHashSet());
        var warning = Assert.Single(root.GetProperty("warnings").EnumerateArray());
        S118ContractAssert.AssertExactKeySet(warning, ViolationKeys, "governance warning row");
        Assert.Equal(0, warning.GetProperty("severity").GetInt32()); // WARNING as the integer 0
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — GET /api/compliance/{employeeId}/compensatory-rest — the string enum.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The compensatory-rest bare array: three INPUT rows drive the STRING
    /// <c>status</c> enum live on ALL THREE declared values (PENDING/GRANTED/EXPIRED —
    /// init.sql:1337) through the matcher; each row the exact 7-member shape;
    /// <c>compensatoryDate</c> in BOTH nullable states. The foreign-employee self-gate is
    /// pinned 403 on both compliance ops.</summary>
    [Fact]
    public async Task CompensatoryRest_Get200_SevenMemberRows_AllThreeStatusValuesLive_ForeignEmployee403()
    {
        var emp = await SeedEmployeeAsync("rest1");
        await SeedCompensatoryRestAsync(emp, "2026-03-02", null, 3.5m, "PENDING");
        await SeedCompensatoryRestAsync(emp, "2026-03-03", "2026-03-10", 2.0m, "GRANTED");
        await SeedCompensatoryRestAsync(emp, "2026-03-04", null, 1.25m, "EXPIRED");

        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/compliance/{emp}/compensatory-rest"),
            "/api/compliance/{employeeId}/compensatory-rest", "get");

        var rows = JsonDocument.Parse(body).RootElement.EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        foreach (var row in rows)
            S118ContractAssert.AssertExactKeySet(row, CompensatoryRestKeys, "compensatory-rest row");
        Assert.Equal(
            new HashSet<string> { "PENDING", "GRANTED", "EXPIRED" }, // all three declared values, LIVE
            rows.Select(r => r.GetProperty("status").GetString()!).ToHashSet());
        var granted = rows.Single(r => r.GetProperty("status").GetString() == "GRANTED");
        Assert.Equal("2026-03-10", granted.GetProperty("compensatoryDate").GetString());
        Assert.Equal(2.0m, granted.GetProperty("hours").GetDecimal());
        var pending = rows.Single(r => r.GetProperty("status").GetString() == "PENDING");
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("compensatoryDate").ValueKind);

        // The self-gate negative half on both compliance ops (per-op pins).
        var alien = await SeedEmployeeAsync("alien1");
        using var alienClient = S120ContractAssert.EmployeeClient(_factory, alien, Org);
        using (var rest = await alienClient.GetAsync($"/api/compliance/{emp}/compensatory-rest"))
            Assert.Equal(403, (int)rest.StatusCode);
        using (var period = await alienClient.GetAsync($"/api/compliance/{emp}/period?year=2026&month=3"))
            Assert.Equal(403, (int)period.StatusCode);
    }

    // ─────────────────────────────── the named-client stub (the ESTABLISHED idiom) ───────────────────────────────

    /// <summary>The RuleEngineAuthForwardingTests.cs:33-39 idiom: keep the REAL named-client
    /// registration (BaseAddress + the forwarding handler) and replace ONLY the named client's
    /// PRIMARY handler — no factory/Hosting change, no real socket.</summary>
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> StubbedHost(string responseBody)
        => _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<HttpClientFactoryOptions>(RuleEngineClient.Name,
                    o => o.HttpMessageHandlerBuilderActions.Add(
                        b => b.PrimaryHandler = new RuleEngineBodyStub(responseBody)))));

    /// <summary>Serves the configured JSON body with 200 for every outgoing rule-engine request.</summary>
    private sealed class RuleEngineBodyStub : HttpMessageHandler
    {
        private readonly string _body;

        public RuleEngineBodyStub(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }

    // ─────────────────────────────── seeds / helpers ───────────────────────────────

    private async Task<string> SeedEmployeeAsync(string suffix)
    {
        var employeeId = "s120c_" + suffix;
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, Org, "AC", "OK24");
        return employeeId;
    }

    private async Task SeedCompensatoryRestAsync(
        string employeeId, string sourceDate, string? compensatoryDate, decimal hours, string status)
    {
        await S120ContractAssert.ExecAsync(_harness.ConnectionString,
            """
            INSERT INTO compensatory_rest (id, employee_id, source_date, compensatory_date, hours, status, created_at)
            VALUES (gen_random_uuid(), @e, @sd::date, @cd::date, @h, @st, NOW())
            """,
            ("e", employeeId),
            ("sd", sourceDate),
            ("cd", (object?)compensatoryDate ?? DBNull.Value),
            ("h", hours),
            ("st", status));
    }

    /// <summary>Read a component schema's declared integer <c>enum</c> set off the committed spec.</summary>
    private HashSet<int> SchemaEnumInts(string schemaName)
        => _spec.GetProperty("components").GetProperty("schemas").GetProperty(schemaName)
            .GetProperty("enum").EnumerateArray().Select(e => e.GetInt32()).ToHashSet();
}
