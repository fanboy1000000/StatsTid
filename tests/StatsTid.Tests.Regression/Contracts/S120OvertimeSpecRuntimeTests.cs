using System.Net.Http;
using System.Text.Json;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S120 / TASK-12002 — the per-route spec≡runtime gate for the per-employee OVERTIME family
/// drained in retrofit Pass 7 (TASK-12000): balance GET (10-member; 404 on missing),
/// compensation-choice GET (ONE record, BOTH <c>source</c> branches — same keys, value-differing,
/// NOT polymorphic), compensation-choice PUT (the 3-member echo), and the compensate POST (the
/// 5-member 200 echo) carrying the <b>LeaderOrAbove floor pins</b>. (The governance GET — the
/// family's 5th op — is gated in <c>S120ComplianceSpecRuntimeTests</c> with its ruling-#3
/// sibling.)
///
/// <para><b>Enum sets (updated S122):</b> <c>compensationModel</c> (AFSPADSERING\|UDBETALING) and
/// <c>compensationType</c> (PAYOUT\|AFSPADSERING) are now DECLARED spec enums — S122 added the DB
/// CHECK authority for the model and ruled the type a handler-enforced authority (the P6 gap
/// flagged here is CLOSED); the matcher now enforces their set-membership. Only <c>source</c>
/// stays REFUSED (raw strings, no authority). These tests pin VALUES; the S122 suites pin the
/// enum-fidelity.</para>
///
/// <para><b>Per-op policy pins:</b> balance/choice GET + choice PUT are EmployeeOrAbove and
/// driven at the Employee floor (positive pins); the choice PUT is additionally SELF-ONLY in the
/// handler (a non-self employee is pinned 403); the compensate POST is <c>LeaderOrAbove</c> —
/// an in-scope Leader SUCCEEDS and the Employee actor is pinned 403 at the policy floor.</para>
///
/// <para><b>UNCONDITIONED pins (Step-0b Reviewer N2, the S119 doubly-pinned precedent):</b> the
/// choice PUT and compensate POST — two of the pass's 3 unconditioned mutations — are sent with
/// NO If-Match/If-None-Match and must SUCCEED, and their 200s serve NO ETag.</para>
///
/// <para><b>Seed discipline (FAIL-002):</b> a FRESH testcontainer per test; org
/// <c>S120OVT1</c>, employees <c>s120o_*</c>. <c>overtime_balances</c> rows are INPUT data
/// (the OvertimeAtomicTests convention). The choice tests run under HK (the init.sql HK/OK24
/// agreement config carries <c>employee_compensation_choice = TRUE</c> — read-asserted).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S120OvertimeSpecRuntimeTests : IAsyncLifetime
{
    private const string Org = "S120OVT1";

    /// <summary>The EXACT 10 members of <c>OvertimeBalanceResponse</c>.</summary>
    private static readonly string[] BalanceKeys =
    {
        "balanceId", "employeeId", "agreementCode", "periodYear", "accumulated",
        "paidOut", "afspadseringUsed", "remaining", "compensationModel", "updatedAt",
    };

    /// <summary>The EXACT 4 members of <c>CompensationChoiceResponse</c> — ONE record for BOTH
    /// branches (the fact-sheet pin: value-differing <c>source</c>, NOT polymorphic).</summary>
    private static readonly string[] ChoiceKeys = { "employeeId", "periodYear", "compensationModel", "source" };

    /// <summary>The EXACT 3 members of <c>CompensationChoiceUpdateResponse</c> (no <c>source</c>).</summary>
    private static readonly string[] ChoiceUpdateKeys = { "employeeId", "periodYear", "compensationModel" };

    /// <summary>The EXACT 5 members of the compensate 200 echo.</summary>
    private static readonly string[] CompensateKeys =
        { "employeeId", "periodYear", "hours", "compensationType", "applied" };

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
    //  Op 1 — GET /api/overtime/{employeeId}/balance — 10-member; 404 on missing.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The 10-member projection through the matcher with every value verbatim (incl.
    /// the computed <c>remaining</c> = accumulated − paidOut − afspadseringUsed), and the
    /// missing-year 404 (the declared non-2xx edge of the zero-FE-caller greenfield op) — both
    /// at the Employee floor.</summary>
    [Fact]
    public async Task Balance_Get200_TenMemberProjection_And404OnMissingYear()
    {
        var emp = await SeedEmployeeAsync("bal1", "AC");
        await SeedOvertimeBalanceAsync(emp, "AC", 2026, accumulated: 12.5m, paidOut: 3m,
            afspadseringUsed: 2.25m, model: "AFSPADSERING");

        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/overtime/{emp}/balance?year=2026"),
            "/api/overtime/{employeeId}/balance", "get");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, BalanceKeys, "overtime balance");
        Assert.NotEqual(Guid.Empty, root.GetProperty("balanceId").GetGuid());
        Assert.Equal(emp, root.GetProperty("employeeId").GetString());
        Assert.Equal("AC", root.GetProperty("agreementCode").GetString());
        Assert.Equal(2026, root.GetProperty("periodYear").GetInt32());
        Assert.Equal(12.5m, root.GetProperty("accumulated").GetDecimal());
        Assert.Equal(3m, root.GetProperty("paidOut").GetDecimal());
        Assert.Equal(2.25m, root.GetProperty("afspadseringUsed").GetDecimal());
        Assert.Equal(7.25m, root.GetProperty("remaining").GetDecimal()); // computed, copied verbatim
        Assert.Equal("AFSPADSERING", root.GetProperty("compensationModel").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("updatedAt").ValueKind);

        using var missing = await employee.GetAsync($"/api/overtime/{emp}/balance?year=1999");
        Assert.Equal(404, (int)missing.StatusCode); // no row for the year
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 2 — GET compensation-choice — BOTH `source` branches, ONE key set.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>BOTH branches of the ONE record against the real endpoint: no balance row ⇒
    /// <c>source: "config_default"</c> (the HK config's AFSPADSERING default); a seeded
    /// UDBETALING balance row for another year ⇒ <c>source: "balance"</c> with the row's model —
    /// the SAME exact 4-key set both times (value-differing, NOT polymorphic; the fact-sheet
    /// pin), both through the matcher at the Employee floor.</summary>
    [Fact]
    public async Task CompensationChoice_Get200_BothSourceBranches_SameKeySet()
    {
        var emp = await SeedEmployeeAsync("cho1", "HK");
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);

        // Branch 1 — no balance row for 2025 ⇒ the config-default fallback.
        var fallback = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(
                HttpMethod.Get, $"/api/overtime/{emp}/compensation-choice?periodYear=2025"),
            "/api/overtime/{employeeId}/compensation-choice", "get");
        var fallbackRoot = JsonDocument.Parse(fallback).RootElement;
        S118ContractAssert.AssertExactKeySet(fallbackRoot, ChoiceKeys, "choice GET (config_default branch)");
        Assert.Equal("config_default", fallbackRoot.GetProperty("source").GetString());
        Assert.Equal("AFSPADSERING", fallbackRoot.GetProperty("compensationModel").GetString()); // HK default
        Assert.Equal(2025, fallbackRoot.GetProperty("periodYear").GetInt32());

        // Branch 2 — a balance row exists for 2026 ⇒ the balance branch, same keys.
        await SeedOvertimeBalanceAsync(emp, "HK", 2026, 0m, 0m, 0m, model: "UDBETALING");
        var fromBalance = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(
                HttpMethod.Get, $"/api/overtime/{emp}/compensation-choice?periodYear=2026"),
            "/api/overtime/{employeeId}/compensation-choice", "get");
        var balanceRoot = JsonDocument.Parse(fromBalance).RootElement;
        S118ContractAssert.AssertExactKeySet(balanceRoot, ChoiceKeys, "choice GET (balance branch)");
        Assert.Equal("balance", balanceRoot.GetProperty("source").GetString());
        Assert.Equal("UDBETALING", balanceRoot.GetProperty("compensationModel").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — PUT compensation-choice — 3-member echo; self-only; UNCONDITIONED.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The write path's 3-member echo (no <c>source</c> — the write always persists to
    /// the balance row): the employee-self PUT succeeds WITHOUT any precondition header
    /// (unconditioned mutation #1 of 3) and serves NO ETag; the choice GET re-read then serves
    /// the <c>balance</c> branch (the PUT created the row — the FE composition flow). A
    /// NON-SELF employee actor is pinned 403 (the handler's own-data-only gate).</summary>
    [Fact]
    public async Task CompensationChoicePut_200_ThreeMemberEcho_SelfOnly_UnconditionedPinned()
    {
        var emp = await SeedEmployeeAsync("cho2", "HK"); // HK: employee_compensation_choice TRUE
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);

        using var response = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/overtime/{emp}/compensation-choice",
            """{ "periodYear": 2026, "compensationModel": "UDBETALING" }""")); // NO precondition header
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)response.StatusCode); // succeeds header-free — the family pin
        S120ContractAssert.AssertUnconditioned(response, "compensation-choice PUT 200");

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(
            _spec, "/api/overtime/{employeeId}/compensation-choice", "put");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "PUT .../compensation-choice (200)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, ChoiceUpdateKeys, "choice PUT 200 echo");
        Assert.Equal(emp, root.GetProperty("employeeId").GetString());
        Assert.Equal(2026, root.GetProperty("periodYear").GetInt32());
        Assert.Equal("UDBETALING", root.GetProperty("compensationModel").GetString());

        // The write landed: the GET now serves the balance branch with the chosen model.
        using var reread = await employee.GetAsync($"/api/overtime/{emp}/compensation-choice?periodYear=2026");
        var rereadRoot = JsonDocument.Parse(await reread.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("balance", rereadRoot.GetProperty("source").GetString());
        Assert.Equal("UDBETALING", rereadRoot.GetProperty("compensationModel").GetString());

        // Self-only: a DIFFERENT employee actor 403s on this employee's choice.
        var alien = await SeedEmployeeAsync("cho2b", "HK");
        using var alienClient = S120ContractAssert.EmployeeClient(_factory, alien, Org);
        using var denied = await alienClient.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/overtime/{emp}/compensation-choice",
            """{ "periodYear": 2026, "compensationModel": "AFSPADSERING" }"""));
        Assert.Equal(403, (int)denied.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 4 — POST compensate — the 5-member echo + the LeaderOrAbove floor pins.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary><b>The LeaderOrAbove floor pins:</b> an in-scope Leader's compensate SUCCEEDS —
    /// the exact-200 5-member echo <c>{employeeId, periodYear, hours, compensationType,
    /// applied}</c> through the matcher, sent WITHOUT any precondition header (unconditioned
    /// mutation #2) and serving NO ETag; the SAME employee whose balance is compensated is 403
    /// at the policy floor. The balance re-read proves the AFSPADSERING adjustment landed
    /// (remaining 10 → 7.5) — response-shaping only ever changed, the P6-fenced write is live.</summary>
    [Fact]
    public async Task Compensate_Post200_FiveMemberEcho_LeaderSucceeds_EmployeeFloor403()
    {
        var emp = await SeedEmployeeAsync("cmp1", "AC");
        await SeedOvertimeBalanceAsync(emp, "AC", 2026, accumulated: 10m, paidOut: 0m,
            afspadseringUsed: 0m, model: "AFSPADSERING");

        using var leader = S120ContractAssert.LeaderClient(_factory, "s120o_leader1", Org);
        using var response = await leader.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/overtime/{emp}/compensate",
            """{ "periodYear": 2026, "hours": 2.5, "compensationType": "AFSPADSERING" }""")); // NO precondition header
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)response.StatusCode); // the Leader floor SUCCESS pin
        S120ContractAssert.AssertUnconditioned(response, "compensate POST 200");

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/overtime/{employeeId}/compensate", "post");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "POST .../compensate (200)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, CompensateKeys, "compensate 200 echo");
        Assert.Equal(emp, root.GetProperty("employeeId").GetString());
        Assert.Equal(2026, root.GetProperty("periodYear").GetInt32());
        Assert.Equal(2.5m, root.GetProperty("hours").GetDecimal());
        Assert.Equal("AFSPADSERING", root.GetProperty("compensationType").GetString());
        Assert.True(root.GetProperty("applied").GetBoolean());

        // The adjustment landed (the P6-fenced in-tx write is live; shaping-only change).
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);
        using var balance = await employee.GetAsync($"/api/overtime/{emp}/balance?year=2026");
        var balanceRoot = JsonDocument.Parse(await balance.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2.5m, balanceRoot.GetProperty("afspadseringUsed").GetDecimal());
        Assert.Equal(7.5m, balanceRoot.GetProperty("remaining").GetDecimal());

        // The floor's negative half: the Employee actor (even self) is 403 at LeaderOrAbove.
        using var denied = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/overtime/{emp}/compensate",
            """{ "periodYear": 2026, "hours": 1, "compensationType": "AFSPADSERING" }"""));
        Assert.Equal(403, (int)denied.StatusCode);
    }

    // ─────────────────────────────── seeds ───────────────────────────────

    private async Task<string> SeedEmployeeAsync(string suffix, string agreementCode)
    {
        var employeeId = "s120o_" + suffix;
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, Org, agreementCode, "OK24");
        return employeeId;
    }

    private async Task SeedOvertimeBalanceAsync(
        string employeeId, string agreementCode, int year,
        decimal accumulated, decimal paidOut, decimal afspadseringUsed, string model)
    {
        await S120ContractAssert.ExecAsync(_harness.ConnectionString,
            """
            INSERT INTO overtime_balances
                (balance_id, employee_id, agreement_code, period_year,
                 accumulated, paid_out, afspadsering_used, compensation_model, updated_at)
            VALUES (gen_random_uuid(), @e, @ac, @y, @acc, @po, @au, @m, NOW())
            """,
            ("e", employeeId), ("ac", agreementCode), ("y", year),
            ("acc", accumulated), ("po", paidOut), ("au", afspadseringUsed), ("m", model));
    }
}
