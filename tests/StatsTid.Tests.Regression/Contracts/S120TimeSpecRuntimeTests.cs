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
/// Pass 7 (TASK-12000): time-entries GET (a BARE ARRAY of the NAMED
/// SharedKernel <c>TimeEntry</c> model — 12 members since S132's ADR-039 <c>sourceStintId</c>,
/// wire exposure owner-ratified 2026-08-25 at the S134-close CI remediation), absences GET (a bare array of the NAMED
/// <c>AbsenceEntry</c> — 6 members; a ZERO-FE-caller greenfield op, typed anyway). S146 R-7: the
/// fourth op, the flex GET (S120 OWNER RULING #1, first instance), is retired with its route. S148 R-20:
/// the time-entries POST (the 201 receipt) is retired with its route too.
///
/// <para><b>Per-op policy pins:</b> the ops are <c>EmployeeOrAbove</c> with the
/// employee-self gate — every op here is DRIVEN by the employee-self client and SUCCEEDS (the
/// positive floor pins), and a FOREIGN employee actor is pinned 403 on each (the self-gate
/// half).</para>
///
/// <para><b>Seed discipline (FAIL-002):</b> a FRESH testcontainer per test (the established
/// Contracts-suite harness — never the compose stack on :5432). ALL ids are S120-prefixed and
/// DISJOINT from every existing suite: org <c>S120TIM1</c>, employees <c>s120t_*</c>. The time-entry
/// and absence projection rows are INPUT-data seeds (the
/// established TeamOverviewAggregateTests convention — absence
/// writes are owned by the Skema save whose rule-engine hop does not exist in this harness).
/// Matcher + Support + S118/S119/S120 asserts consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S120TimeSpecRuntimeTests : IAsyncLifetime
{
    private const string Org = "S120TIM1";

    /// <summary>The EXACT 12 camelCase members of the NAMED SharedKernel <c>TimeEntry</c> model
    /// (the wire shape by construction — the PAT-012 named-model rule).</summary>
    private static readonly string[] TimeEntryKeys =
    {
        "employeeId", "date", "hours", "startTime", "endTime", "taskId",
        "activityType", "agreementCode", "okVersion", "registeredAt", "voluntaryUnsocialHours",
        // S132 / ADR-039: links a midnight-split projection row back to its source stint;
        // null for a non-split entry. Wire exposure ratified by the owner 2026-08-25.
        "sourceStintId",
    };

    /// <summary>The EXACT 6 members of the NAMED SharedKernel <c>AbsenceEntry</c> model.</summary>
    private static readonly string[] AbsenceEntryKeys =
        { "employeeId", "date", "absenceType", "hours", "agreementCode", "okVersion" };

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
    //  Op 1 — GET /api/time-entries/{id} (S148 R-20: the POST op, with its 201 receipt, is retired).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Kind R (regression pin, holds today). The time-entries GET serves a seeded
    /// <c>time_entries_projection</c> row as a BARE ARRAY of the exact 12-member NAMED
    /// <c>TimeEntry</c> shape, matched against the committed spec at the Employee floor (positive
    /// policy pin). S148 R-20 re-point: the row is a SQL INPUT seed (start 08:00, end 15:24, AC,
    /// OK24, no task), because <c>POST /api/time-entries</c> and its 201 receipt are retired; the
    /// receipt half and <c>ReceiptKeys</c> went with the route. The discriminating assertions are
    /// the exact key set and the row's values, so a dropped or renamed member fails here.</summary>
    [Fact]
    public async Task TimeEntries_Get200_BareArray_SeededRow_NamedTimeEntryModel()
    {
        var emp = await SeedEmployeeAsync("post1");
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);

        await S120ContractAssert.ExecAsync(_harness.ConnectionString,
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, start_time, end_time, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES (gen_random_uuid(), @e, '2026-03-02', 7.4, '08:00', '15:24', 'AC', 'OK24', FALSE, NOW(), 9120002)
            """, ("e", emp));

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
        // ADR-039: a plain (non-midnight-crossing) entry is not a split row — no source stint.
        Assert.Equal(JsonValueKind.Null, row.GetProperty("sourceStintId").ValueKind);
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
    //  S146 R-7 (TASK-14607-T): Op 4, GET /api/flex-balance/{employeeId}, is RETIRED. The route,
    //  FlexBalanceResponse and the FlexBalanceUpdated event it read were deleted (dead since S120:
    //  no emission site). S120 OWNER RULING #1 (branch normalisation) is a CLASS ruling and stands;
    //  its FIRST INSTANCE, the flex no-history/with-history pair, is retired with the route. The
    //  retired facts: Flex_Get200_NoHistoryBranch_NormalizedOneShape_Ruling1DeltaPin,
    //  Flex_Get200_WithHistoryBranch_LatestEventValues_ByteFaithful and
    //  Gate_IsRed_OnInjectedPhantomRequiredMember_AndGreenOnTheCommittedTruth (the injected-lie
    //  technique lives on in the S117-S119 siblings). The /summary flex shape is pinned in
    //  S120BalanceSpecRuntimeTests and FlexBalanceReadTests.
    // ════════════════════════════════════════════════════════════════════════════════

    // ════════════════════════════════════════════════════════════════════════════════
    //  The P7 self-gate pins — a FOREIGN employee actor, 403 on the two remaining ops (S146: the flex op is retired; S148 R-20: the time-entries POST is retired).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The policy map's negative half: the SAME Employee role that succeeds on
    /// self-access is 403 on a FOREIGN employee's id across the two remaining ops (the employee-self
    /// gate; per-op pins, not a generalization).</summary>
    [Fact]
    public async Task ForeignEmployee_403_OnTheTwoRemainingOps_SelfGatePins()
    {
        var target = await SeedEmployeeAsync("tgt1");
        var alien = await SeedEmployeeAsync("alien1");
        using var alienClient = S120ContractAssert.EmployeeClient(_factory, alien, Org);

        foreach (var url in new[]
        {
            $"/api/time-entries/{target}",
            $"/api/absences/{target}",
        })
        {
            using var get = await alienClient.SendAsync(
                SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, url));
            Assert.Equal(403, (int)get.StatusCode);
        }
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
}
