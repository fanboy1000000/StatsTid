using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S120 / TASK-12002 — the per-route spec≡runtime gate for the TIME family drained in retrofit
/// Pass 7 (TASK-12000): time-entries POST (the 201 <c>{eventId, streamId}</c> receipt — the
/// event-sourced write's receipt, P3-fenced) + time-entries GET (a BARE ARRAY of the NAMED
/// SharedKernel <c>TimeEntry</c> model — 11 members), absences GET (a bare array of the NAMED
/// <c>AbsenceEntry</c> — 6 members; a ZERO-FE-caller greenfield op, typed anyway), and the flex
/// GET carrying <b>OWNER RULING #1</b> (branch-normalization class, 1st instance): the
/// no-history branch serves the normalized ONE shape — all 5 keys present, the 3 history keys
/// null, NO vestigial <c>message</c> key — while the with-history branch is byte-faithful to
/// the pre-S120 wire.
///
/// <para><b>Per-op policy pins:</b> all four ops are <c>EmployeeOrAbove</c> with the
/// employee-self gate — every op here is DRIVEN by the employee-self client and SUCCEEDS (the
/// positive floor pins), and a FOREIGN employee actor is pinned 403 on all four (the self-gate
/// half).</para>
///
/// <para><b>UNCONDITIONED pin (Step-0b Reviewer N2):</b> the POST is sent with NO
/// If-Match/If-None-Match and must succeed, and its 201 serves NO ETag.</para>
///
/// <para><b>Seed discipline (FAIL-002):</b> a FRESH testcontainer per test (the established
/// Contracts-suite harness — never the compose stack on :5432). ALL ids are S120-prefixed and
/// DISJOINT from every existing suite: org <c>S120TIM1</c>, employees <c>s120t_*</c>. Time
/// entries are created through the REAL POST (read-your-write choreography); the absence
/// projection row and the flex <c>FlexBalanceUpdated</c> events are INPUT-data seeds (the
/// established TeamOverviewAggregateTests convention — flex has no write endpoint; absence
/// writes are owned by the Skema save whose rule-engine hop does not exist in this harness).
/// Matcher + Support + S118/S119/S120 asserts consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S120TimeSpecRuntimeTests : IAsyncLifetime
{
    private const string Org = "S120TIM1";

    /// <summary>The EXACT 2 members of <c>TimeEntryCreatedResponse</c> (the 201 receipt).</summary>
    private static readonly string[] ReceiptKeys = { "eventId", "streamId" };

    /// <summary>The EXACT 11 camelCase members of the NAMED SharedKernel <c>TimeEntry</c> model
    /// (the wire shape by construction — the PAT-012 named-model rule).</summary>
    private static readonly string[] TimeEntryKeys =
    {
        "employeeId", "date", "hours", "startTime", "endTime", "taskId",
        "activityType", "agreementCode", "okVersion", "registeredAt", "voluntaryUnsocialHours",
    };

    /// <summary>The EXACT 6 members of the NAMED SharedKernel <c>AbsenceEntry</c> model.</summary>
    private static readonly string[] AbsenceEntryKeys =
        { "employeeId", "date", "absenceType", "hours", "agreementCode", "okVersion" };

    /// <summary>The EXACT 5 members of the RULING-#1 normalized <c>FlexBalanceResponse</c> —
    /// the vestigial <c>message</c> key is NOT here, and the exact-key-set assert proves its
    /// absence on the live no-history wire.</summary>
    private static readonly string[] FlexKeys =
        { "employeeId", "balance", "previousBalance", "delta", "reason" };

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
    //  Ops 1+2 — POST /api/time-entries (201 receipt) → GET /api/time-entries/{id}.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The event-sourced write's receipt: exact-201 (a 200 here is RED) + matcher +
    /// the EXACT 2-member <c>{eventId, streamId}</c> with the ADR-018 D6 consolidated stream id
    /// literal. UNCONDITIONED pinned (no precondition header sent; NO ETag served). The GET
    /// re-read then serves the just-written entry (the ADR-018 D12 read-your-write choreography)
    /// as a BARE ARRAY of the exact 11-member NAMED <c>TimeEntry</c> shape — both ops proven on
    /// live bytes in one flow, driven at the Employee floor (positive policy pins ×2).</summary>
    [Fact]
    public async Task TimeEntryCreate_Post201Exact_TwoMemberReceipt_ThenGetServesTheEntry()
    {
        var emp = await SeedEmployeeAsync("post1");
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);

        using var response = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/time-entries",
            $$"""
            { "employeeId": "{{emp}}", "date": "2026-03-02", "hours": 7.4,
              "startTime": "08:00:00", "endTime": "15:24:00", "agreementCode": "AC" }
            """)); // NO precondition header — the unconditioned pin, half 1
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(201, (int)response.StatusCode); // the EXACT status — a 200 here is RED
        S120ContractAssert.AssertUnconditioned(response, "time-entries POST 201");

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/time-entries", "post");
        Assert.Equal(201, truth.StatusCode);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body, "POST /api/time-entries (201)");

        var receipt = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(receipt, ReceiptKeys, "time-entries POST 201 receipt");
        Assert.NotEqual(Guid.Empty, receipt.GetProperty("eventId").GetGuid());
        Assert.Equal($"employee-{emp}", receipt.GetProperty("streamId").GetString()); // ADR-018 D6 literal

        // The GET re-read — read-your-write through the atomic in-tx projection (ADR-018 D12).
        var listBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/time-entries/{emp}"),
            "/api/time-entries/{employeeId}", "get");

        var rows = JsonDocument.Parse(listBody).RootElement;
        Assert.Equal(JsonValueKind.Array, rows.ValueKind); // the bare-array headline
        var row = Assert.Single(rows.EnumerateArray());
        S118ContractAssert.AssertExactKeySet(row, TimeEntryKeys, "time-entries GET row (named TimeEntry model)");
        Assert.Equal(emp, row.GetProperty("employeeId").GetString());
        Assert.Equal("2026-03-02", row.GetProperty("date").GetString());
        Assert.Equal(7.4m, row.GetProperty("hours").GetDecimal());
        Assert.Equal("AC", row.GetProperty("agreementCode").GetString());
        Assert.Equal("OK24", row.GetProperty("okVersion").GetString()); // server-resolved (ADR-003)
        Assert.StartsWith("08:00", row.GetProperty("startTime").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("taskId").ValueKind);
        Assert.False(row.GetProperty("voluntaryUnsocialHours").GetBoolean());
        Assert.Equal(JsonValueKind.String, row.GetProperty("registeredAt").ValueKind);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — GET /api/absences/{employeeId} (greenfield; both empty + populated).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The greenfield zero-caller op: the EMPTY bare array for a fresh employee, then —
    /// after seeding one absences_projection INPUT row — the exact 6-member NAMED
    /// <c>AbsenceEntry</c> row (the projection's <c>feriedage</c> column is NOT part of this
    /// model; the exact-key-set proves it never leaks). Both reads through the matcher at the
    /// Employee floor.</summary>
    [Fact]
    public async Task Absences_Get200_BareArray_EmptyThenSeededRow_NamedAbsenceEntryModel()
    {
        var emp = await SeedEmployeeAsync("abs1");
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);

        var before = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/absences/{emp}"),
            "/api/absences/{employeeId}", "get");
        Assert.Equal(0, JsonDocument.Parse(before).RootElement.GetArrayLength()); // greenfield-empty branch

        await S120ContractAssert.ExecAsync(_harness.ConnectionString,
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage,
                 agreement_code, ok_version, occurred_at, outbox_id)
            VALUES (gen_random_uuid(), @e, '2026-03-04', 'VACATION', 7.4, 1.0, 'AC', 'OK24', NOW(), 9120001)
            """, ("e", emp));

        var after = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/absences/{emp}"),
            "/api/absences/{employeeId}", "get");
        var row = Assert.Single(JsonDocument.Parse(after).RootElement.EnumerateArray());
        S118ContractAssert.AssertExactKeySet(row, AbsenceEntryKeys, "absences GET row (named AbsenceEntry model)");
        Assert.Equal(emp, row.GetProperty("employeeId").GetString());
        Assert.Equal("2026-03-04", row.GetProperty("date").GetString());
        Assert.Equal("VACATION", row.GetProperty("absenceType").GetString());
        Assert.Equal(7.4m, row.GetProperty("hours").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 4 — GET /api/flex-balance/{employeeId} — the RULING-#1 DELTA pins.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary><b>RULING #1, the no-history branch (the DELTA — never byte-asserted against the
    /// old wire):</b> a fresh employee with NO <c>FlexBalanceUpdated</c> history serves the
    /// normalized ONE shape — ALL 5 keys present, <c>balance: 0</c>, the 3 history keys JSON
    /// null, and NO <c>message</c> key (the vestigial member DIED; the exact-key-set assert
    /// fails on its resurrection AND on any re-omission of the null-filled keys).</summary>
    [Fact]
    public async Task Flex_Get200_NoHistoryBranch_NormalizedOneShape_Ruling1DeltaPin()
    {
        var emp = await SeedEmployeeAsync("flx1");
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/flex-balance/{emp}"),
            "/api/flex-balance/{employeeId}", "get");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, FlexKeys, "flex no-history branch (ruling #1)");
        Assert.Equal(emp, root.GetProperty("employeeId").GetString());
        Assert.Equal(0m, root.GetProperty("balance").GetDecimal());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("previousBalance").ValueKind); // null-filled
        Assert.Equal(JsonValueKind.Null, root.GetProperty("delta").ValueKind);           // null-filled
        Assert.Equal(JsonValueKind.Null, root.GetProperty("reason").ValueKind);          // null-filled
        // NO `message` key: proven by the exact-key-set assert above (extra keys fail, named).
    }

    /// <summary><b>RULING #1, the with-history branch (byte-faithful — NOT part of the delta):</b>
    /// two seeded <c>FlexBalanceUpdated</c> events (INPUT data on the employee stream; the
    /// TeamOverviewAggregateTests seed convention) — the endpoint serves the LATEST event's
    /// values on the SAME 5-key shape, every member non-null.</summary>
    [Fact]
    public async Task Flex_Get200_WithHistoryBranch_LatestEventValues_ByteFaithful()
    {
        var emp = await SeedEmployeeAsync("flx2");
        await SeedFlexEventAsync(emp, version: 1, previous: 0m, next: 3.5m, reason: "S120 fleksopbygning");
        await SeedFlexEventAsync(emp, version: 2, previous: 3.5m, next: 5.25m, reason: "S120 fleksregulering");

        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/flex-balance/{emp}"),
            "/api/flex-balance/{employeeId}", "get");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, FlexKeys, "flex with-history branch");
        Assert.Equal(emp, root.GetProperty("employeeId").GetString());
        Assert.Equal(5.25m, root.GetProperty("balance").GetDecimal());          // the LATEST event
        Assert.Equal(3.5m, root.GetProperty("previousBalance").GetDecimal());
        Assert.Equal(1.75m, root.GetProperty("delta").GetDecimal());
        Assert.Equal("S120 fleksregulering", root.GetProperty("reason").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The P7 self-gate pins — a FOREIGN employee actor, 403 on all four ops.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The policy map's negative half: the SAME Employee role that succeeds on
    /// self-access is 403 on a FOREIGN employee's id across all four ops (the employee-self
    /// gate; per-op pins, not a generalization).</summary>
    [Fact]
    public async Task ForeignEmployee_403_OnAllFourOps_SelfGatePins()
    {
        var target = await SeedEmployeeAsync("tgt1");
        var alien = await SeedEmployeeAsync("alien1");
        using var alienClient = S120ContractAssert.EmployeeClient(_factory, alien, Org);

        using (var post = await alienClient.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/time-entries",
            $$"""{ "employeeId": "{{target}}", "date": "2026-03-02", "hours": 7.4, "agreementCode": "AC" }""")))
            Assert.Equal(403, (int)post.StatusCode);

        foreach (var url in new[]
        {
            $"/api/time-entries/{target}",
            $"/api/absences/{target}",
            $"/api/flex-balance/{target}",
        })
        {
            using var get = await alienClient.SendAsync(
                SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, url));
            Assert.Equal(403, (int)get.StatusCode);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The RED-on-lie proof (the S120 pass's injected-lie demonstration).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The established injected-lie technique (S117/S118/S119): the REAL flex 200
    /// passes against the COMMITTED truth (GREEN), then the SAME body is matched against an
    /// IN-MEMORY corrupted spec whose <c>FlexBalanceResponse</c> gains a phantom
    /// <c>required</c> member — the matcher MUST go RED through the required-fidelity path with
    /// the phantom member NAMED. The committed spec on disk is never touched (revert-free by
    /// construction).</summary>
    [Fact]
    public async Task Gate_IsRed_OnInjectedPhantomRequiredMember_AndGreenOnTheCommittedTruth()
    {
        var emp = await SeedEmployeeAsync("red1");
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);
        using var response = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Get, $"/api/flex-balance/{emp}"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)response.StatusCode);

        const string path = "/api/flex-balance/{employeeId}";

        // GREEN — the committed truth passes on the real 200.
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, path, "get");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "truth");

        // RED — the same response against the spec with a phantom required member injected.
        var lieNode = JsonNode.Parse(_spec.GetRawText())!;
        var schema = lieNode["components"]!["schemas"]!["StatsTid.Backend.Api.Contracts.FlexBalanceResponse"]!;
        ((JsonArray)schema["required"]!).Add("s120PhantomMember");
        var lieSpec = JsonDocument.Parse(lieNode.ToJsonString()).RootElement.Clone();

        var lieContract = SpecRuntimeMatcher.ResolveSuccessContract(lieSpec, path, "get");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertSuccessMatches(lieSpec, lieContract, 200, body, "injected-required-lie"));

        Assert.Contains("s120PhantomMember", ex.Message, StringComparison.Ordinal);
        Assert.Contains("REQUIRED", ex.Message, StringComparison.Ordinal); // the required-fidelity path, not a kind check
    }

    // ─────────────────────────────── seeds / helpers ───────────────────────────────

    /// <summary>Employee INPUT rows (users + employee_profiles + user_agreement_codes) via the
    /// canonical <see cref="RegressionSeed"/>. Ids are s120t_-prefixed, org S120TIM1.</summary>
    private async Task<string> SeedEmployeeAsync(string suffix)
    {
        var employeeId = "s120t_" + suffix;
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, Org, "AC", "OK24");
        return employeeId;
    }

    /// <summary>Seed one <c>FlexBalanceUpdated</c> event on the employee stream — INPUT data
    /// (flex has NO write endpoint; the event stream IS its store). camelCase data matching the
    /// EventSerializer stored shape, all 5 required members present (the
    /// TeamOverviewAggregateTests convention).</summary>
    private async Task SeedFlexEventAsync(string employeeId, int version, decimal previous, decimal next, string reason)
    {
        var streamId = $"employee-{employeeId}";
        await S120ContractAssert.ExecAsync(_harness.ConnectionString,
            "INSERT INTO event_streams (stream_id) VALUES (@s) ON CONFLICT DO NOTHING", ("s", streamId));

        var data =
            $"{{\"eventId\":\"{Guid.NewGuid()}\",\"employeeId\":\"{employeeId}\"," +
            $"\"previousBalance\":{previous.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"newBalance\":{next.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"delta\":{(next - previous).ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"reason\":\"{reason}\"}}";
        await S120ContractAssert.ExecAsync(_harness.ConnectionString,
            """
            INSERT INTO events (event_id, stream_id, stream_version, event_type, data, occurred_at)
            VALUES (gen_random_uuid(), @s, @v, 'FlexBalanceUpdated', @d::jsonb, NOW())
            """,
            ("s", streamId), ("v", version), ("d", data));
    }
}
