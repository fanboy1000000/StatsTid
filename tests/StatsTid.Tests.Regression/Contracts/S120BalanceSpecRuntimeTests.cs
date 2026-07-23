using System.Data;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Balance; // FixedTimeProvider
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S120 / TASK-12002 — the per-route spec≡runtime gate for the BALANCE family drained in
/// retrofit Pass 7 (TASK-12000): summary (the 12-scalar + <c>entitlements[]</c> +
/// nullable-complex <c>overtimeBalance</c> composite), series (nested <c>points[]</c>), and
/// year-overview carrying <b>OWNER RULING #2</b> (branch-normalization class, 2nd instance):
/// the empty-config category row now ALSO carries <c>settlement: null</c> (pre-S120 it omitted
/// the key).
///
/// <para><b>The allOf both-branch proofs (3 of the pass's 4 wrapper members live here):</b>
/// <c>summary.entitlements[].settlement</c> (list-nested application #1),
/// <c>summary.overtimeBalance</c> (application #2) and
/// <c>yearOverview.categories[].settlement</c> (list-nested application #3) are each driven
/// NULL and POPULATED against the REAL endpoint — the settled branches via the S117
/// settled-year choreography (<c>VacationSettlementService.SettleAsync</c> driven the exact
/// <c>SettlementCloseService</c> way; never SQL-faked settlement state), the overtime branch
/// via an <c>overtime_balances</c> INPUT row. The matcher recurses THROUGH the wrapper on
/// every populated branch (inner required + <c>state</c>/<c>reviewDisposition</c> enum
/// fidelity on live values).</para>
///
/// <para><b>TimeProvider determinism (the year-overview suites' pattern):</b> the year-overview
/// host runs a <see cref="FixedTimeProvider"/> at 2026-03-05 (the S117 settlement anchor —
/// ferieår 2021 is firmly past, and the served <c>today</c> is pinned byte-exact). The
/// summary/series handlers are today-independent (anchored on the requested year/month) and
/// ride the same host.</para>
///
/// <para><b>Seed discipline (FAIL-002):</b> a FRESH testcontainer per test; ALL ids
/// S120-prefixed and DISJOINT from every existing Balance suite (org <c>S120BAL1</c>,
/// employees <c>s120b_*</c> vs their <c>emp001</c>/<c>emp_s65_*</c>). Balance <c>used</c> rows
/// are INPUT data (the operand the legal partition reads — the S117 rule); the init.sql
/// entitlement/agreement config seeds are READ-ASSERTED ONLY.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S120BalanceSpecRuntimeTests : IAsyncLifetime
{
    private const string Org = "S120BAL1";
    private const string VacationType = "VACATION";

    // The S117 settlement anchor: fixed clock 2026-03-05; ferieår 2021 (Sep 2021 .. Aug 2022)
    // is firmly past — YEAR_END settles of 2021 are due.
    private static readonly DateOnly Clock = new(2026, 3, 5);
    private const int ClosedYear = 2021;

    /// <summary>The EXACT 14 top-level members of <c>BalanceSummaryResponse</c>.</summary>
    private static readonly string[] SummaryKeys =
    {
        "employeeId", "year", "month", "flexBalance", "flexDelta", "vacationDaysUsed",
        "vacationDaysEntitlement", "normHoursExpected", "normHoursActual", "overtimeHours",
        "agreementCode", "hasMerarbejde", "entitlements", "overtimeBalance",
    };

    /// <summary>The EXACT 10 members of a <c>BalanceEntitlementRow</c>.</summary>
    private static readonly string[] EntitlementRowKeys =
    {
        "type", "label", "totalQuota", "earned", "used", "planned",
        "carryoverIn", "remaining", "entitlementYear", "settlement",
    };

    /// <summary>The EXACT 5 members of <c>BalanceSummaryOvertimeInfo</c>.</summary>
    private static readonly string[] SummaryOvertimeKeys =
        { "accumulated", "paidOut", "afspadseringUsed", "remaining", "compensationModel" };

    /// <summary>The shared EXACT 7-key <c>SettlementDispositionInfo</c> (ONE record ×3 sites).</summary>
    private static readonly string[] DispositionKeys =
    {
        "state", "transferDays", "payoutDays", "forfeitDays",
        "forfeitPending", "reviewDisposition", "claimDispositionDays",
    };

    /// <summary>The EXACT 4 members of the series envelope.</summary>
    private static readonly string[] SeriesEnvelopeKeys = { "employeeId", "year", "month", "series" };

    /// <summary>The EXACT 6 members of a <c>BalanceSeriesItem</c>.</summary>
    private static readonly string[] SeriesItemKeys =
        { "type", "label", "annualQuota", "entitlementYear", "ferieaarStart", "points" };

    /// <summary>The EXACT 3 members of a <c>BalanceSeriesPoint</c>.</summary>
    private static readonly string[] SeriesPointKeys = { "monthEnd", "earned", "isSelected" };

    /// <summary>The EXACT 7 members of a <c>YearOverviewCategory</c> — <c>settlement</c> is now
    /// ALWAYS present (ruling #2 normalizes the empty-config branch onto this one shape).</summary>
    private static readonly string[] CategoryKeys =
        { "type", "label", "saldo", "afholdt", "expiring", "boundaryMonth", "settlement" };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _fixedHost = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _fixedHost = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Clock))));
        _ = _fixedHost.CreateClient(); // boot seeders — absent-state fixtures seed AFTER this (S63 lesson)
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _fixedHost?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 1 — GET /summary: the composite; overtimeBalance BOTH branches; settlement null.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The 14-member composite through the matcher; the 5 AC/OK24 entitlement rows each
    /// the exact 10-member shape with <c>settlement</c> served NULL (the list-nested wrapper's
    /// null branch, application #1); <c>overtimeBalance</c> NULL (no overtime_balances row —
    /// wrapper application #2's null branch), then POPULATED (exact 5-member, values verbatim
    /// incl. the computed <c>remaining</c>) after the INPUT row lands — BOTH branches of both
    /// wrapper members on live bytes, at the Employee floor.</summary>
    [Fact]
    public async Task Summary_Get200_Composite_OvertimeBalanceBothBranches_SettlementNullBranch()
    {
        var emp = await SeedEmployeeAsync("sum1");
        using var employee = S120ContractAssert.EmployeeClient(_fixedHost, emp, Org);

        var before = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/balance/{emp}/summary?year=2026&month=3"),
            "/api/balance/{employeeId}/summary", "get");

        var root = JsonDocument.Parse(before).RootElement;
        S118ContractAssert.AssertExactKeySet(root, SummaryKeys, "balance summary (unsettled)");
        Assert.Equal(emp, root.GetProperty("employeeId").GetString());
        Assert.Equal(2026, root.GetProperty("year").GetInt32());
        Assert.Equal(3, root.GetProperty("month").GetInt32());
        Assert.Equal("AC", root.GetProperty("agreementCode").GetString());
        Assert.True(root.GetProperty("hasMerarbejde").GetBoolean()); // AC agreement_configs seed

        var rows = root.GetProperty("entitlements").EnumerateArray().ToList();
        Assert.Equal(5, rows.Count); // the 5 AC/OK24 seeded entitlement types (read-asserted)
        foreach (var row in rows)
        {
            S118ContractAssert.AssertExactKeySet(row, EntitlementRowKeys, "entitlement row");
            Assert.Equal(JsonValueKind.Null, row.GetProperty("settlement").ValueKind); // wrapper #1 NULL branch
        }
        var vacation = rows.Single(r => r.GetProperty("type").GetString() == VacationType);
        Assert.Equal(2025, vacation.GetProperty("entitlementYear").GetInt32()); // ferieår at 2026-03-31 (reset 9)

        Assert.Equal(JsonValueKind.Null, root.GetProperty("overtimeBalance").ValueKind); // wrapper #2 NULL branch

        // The POPULATED overtimeBalance branch: one INPUT row → the exact 5-member sub-object.
        await SeedOvertimeBalanceAsync(emp, 2026, accumulated: 10.5m, paidOut: 2m, afspadseringUsed: 1.25m);
        var after = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/balance/{emp}/summary?year=2026&month=3"),
            "/api/balance/{employeeId}/summary", "get");
        var overtime = JsonDocument.Parse(after).RootElement.GetProperty("overtimeBalance");
        Assert.Equal(JsonValueKind.Object, overtime.ValueKind); // wrapper #2 POPULATED branch
        S118ContractAssert.AssertExactKeySet(overtime, SummaryOvertimeKeys, "summary overtimeBalance");
        Assert.Equal(10.5m, overtime.GetProperty("accumulated").GetDecimal());
        Assert.Equal(2m, overtime.GetProperty("paidOut").GetDecimal());
        Assert.Equal(1.25m, overtime.GetProperty("afspadseringUsed").GetDecimal());
        Assert.Equal(7.25m, overtime.GetProperty("remaining").GetDecimal()); // computed, copied verbatim
        Assert.Equal("AFSPADSERING", overtime.GetProperty("compensationModel").GetString());
    }

    /// <summary>The settled-year branch (the S117 settlement choreography — the REAL
    /// <c>VacationSettlementService.SettleAsync</c> pass, never SQL-faked state): balance
    /// <c>used</c> = 20.75 for ferieår 2021 ⇒ disposable 4.25 ≤ cap 5 ⇒ SETTLED with
    /// <c>payout_days</c> 4.25 (the legal partition itself produces the bucket). The summary for
    /// (2021, 9) then serves the VACATION row's <c>settlement</c> POPULATED — the matcher
    /// recursed THROUGH the list-nested wrapper (inner required + the <c>state</c> enum on the
    /// live "SETTLED") — while every other row's <c>settlement</c> stays NULL in the SAME
    /// response (both branches of wrapper member #1 in one op).</summary>
    [Fact]
    public async Task Summary_Get200_SettledYear_SettlementPopulatedThroughTheWrapper()
    {
        var emp = await SeedEmployeeAsync("sum2");
        await SeedBalanceAsync(emp, ClosedYear, used: 20.75m);
        await SettleYearEndAsync(emp, ClosedYear);

        using var employee = S120ContractAssert.EmployeeClient(_fixedHost, emp, Org);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(
                HttpMethod.Get, $"/api/balance/{emp}/summary?year={ClosedYear}&month=9"),
            "/api/balance/{employeeId}/summary", "get");

        var rows = JsonDocument.Parse(body).RootElement.GetProperty("entitlements").EnumerateArray().ToList();
        var vacation = rows.Single(r => r.GetProperty("type").GetString() == VacationType);
        Assert.Equal(ClosedYear, vacation.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(0m, vacation.GetProperty("remaining").GetDecimal()); // SETTLED ⇒ fully disposed

        var settlement = vacation.GetProperty("settlement");
        Assert.Equal(JsonValueKind.Object, settlement.ValueKind); // the POPULATED wrapper branch, LIVE
        S118ContractAssert.AssertExactKeySet(settlement, DispositionKeys, "summary settled disposition");
        Assert.Equal("SETTLED", settlement.GetProperty("state").GetString());   // in the declared enum set
        Assert.Equal(0m, settlement.GetProperty("transferDays").GetDecimal());  // §21 — no transfer agreement
        Assert.Equal(4.25m, settlement.GetProperty("payoutDays").GetDecimal()); // §24 — the partition's bucket
        Assert.Equal(0m, settlement.GetProperty("forfeitDays").GetDecimal());   // §34 — nothing beyond the cap
        Assert.False(settlement.GetProperty("forfeitPending").GetBoolean());
        Assert.Equal(JsonValueKind.Null, settlement.GetProperty("reviewDisposition").ValueKind);
        Assert.Equal(JsonValueKind.Null, settlement.GetProperty("claimDispositionDays").ValueKind);

        // The other rows stay on the NULL branch in the SAME response.
        foreach (var other in rows.Where(r => r.GetProperty("type").GetString() != VacationType))
            Assert.Equal(JsonValueKind.Null, other.GetProperty("settlement").ValueKind);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 2 — GET /series: the nested points envelope.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The series envelope through the matcher: exact 4-member root; the 2
    /// MONTHLY_ACCRUAL series (VACATION + SPECIAL_HOLIDAY — IMMEDIATE types serve NO series);
    /// each item the exact 6-member shape with 12 exact 3-member points. VACATION is anchored
    /// at ferieår 2025 (<c>ferieaarStart</c> "2025-09-01") with EXACTLY ONE selected point —
    /// the requested (2026, 3) — carrying the ADR-031 flat earned 14.58 (25 × 7/12).
    /// SPECIAL_HOLIDAY pins the S80 two-calendar-year geometry BY DESIGN: the March-2026 query
    /// falls in the TAKING window of accrual year 2024 (1 May 2025 – 30 Apr 2026), so its
    /// curve renders Jan–Dec 2024 and carries NO selected point (the handler-documented
    /// consequence: the curve shows when the days were EARNED, the query is when you LOOK).</summary>
    [Fact]
    public async Task Series_Get200_NestedPointsEnvelope_MonthlyAccrualOnly()
    {
        var emp = await SeedEmployeeAsync("ser1");
        using var employee = S120ContractAssert.EmployeeClient(_fixedHost, emp, Org);

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/balance/{emp}/series?year=2026&month=3"),
            "/api/balance/{employeeId}/series", "get");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, SeriesEnvelopeKeys, "series envelope");
        Assert.Equal(emp, root.GetProperty("employeeId").GetString());

        var series = root.GetProperty("series").EnumerateArray().ToList();
        Assert.Equal(2, series.Count); // MONTHLY_ACCRUAL only: VACATION + SPECIAL_HOLIDAY
        foreach (var item in series)
        {
            S118ContractAssert.AssertExactKeySet(item, SeriesItemKeys, "series item");
            var points = item.GetProperty("points").EnumerateArray().ToList();
            Assert.Equal(12, points.Count);
            foreach (var point in points)
                S118ContractAssert.AssertExactKeySet(point, SeriesPointKeys, "series point");
        }

        // VACATION: the accrual window CONTAINS the queried month ⇒ exactly one "now" point.
        var vacation = series.Single(s => s.GetProperty("type").GetString() == VacationType);
        Assert.Equal(2025, vacation.GetProperty("entitlementYear").GetInt32());
        Assert.Equal("2025-09-01", vacation.GetProperty("ferieaarStart").GetString());
        Assert.Equal(25m, vacation.GetProperty("annualQuota").GetDecimal());
        var selected = Assert.Single(vacation.GetProperty("points").EnumerateArray()
            .Where(p => p.GetProperty("isSelected").GetBoolean()));
        Assert.Equal("2026-03-31", selected.GetProperty("monthEnd").GetString());
        Assert.Equal(14.58m, selected.GetProperty("earned").GetDecimal()); // 25 × 7/12, ADR-031 flat

        // SPECIAL_HOLIDAY: the taking-window query resolves the 2024 ACCRUAL year — the curve
        // lies wholly in 2024 and the queried month coincides with NO point (S80 geometry pin).
        var specialHoliday = series.Single(s => s.GetProperty("type").GetString() == "SPECIAL_HOLIDAY");
        Assert.Equal(2024, specialHoliday.GetProperty("entitlementYear").GetInt32());
        Assert.Equal("2024-01-01", specialHoliday.GetProperty("ferieaarStart").GetString());
        Assert.DoesNotContain(specialHoliday.GetProperty("points").EnumerateArray(),
            p => p.GetProperty("isSelected").GetBoolean());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — GET /year-overview: RULING #2 + the categories[].settlement both branches.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary><b>RULING #2, the empty-config branch (the DELTA — never byte-asserted against
    /// the old wire):</b> a profile-less bare user under a fictitious agreement with NO
    /// entitlement_configs row drives ALL FOUR categories onto the graceful empty branch (the
    /// S65 reachable-representative recipe — profile-less so the unknown code never reaches a
    /// throwing config resolve; seeded AFTER the last host boot per the S63 boot-order lesson).
    /// Every category row now carries the EXACT 7-member shape INCLUDING <c>settlement</c>,
    /// served as JSON null — pre-S120 the key was OMITTED on this branch, so the exact-key-set
    /// assert is the ruled delta's pin. Every other member of the empty row is byte-identical
    /// (saldo 12 nulls, afholdt 12 zeros, expiring 0, boundaryMonth 12).</summary>
    [Fact]
    public async Task YearOverview_EmptyConfigCategoryRows_CarrySettlementNull_Ruling2DeltaPin()
    {
        const string noConfigAgreement = "S120_NOCONF";
        const string emp = "s120b_bare1";
        await InsertBareUserAsync(emp, noConfigAgreement); // AFTER the InitializeAsync host boots

        using var employee = S120ContractAssert.EmployeeClient(_fixedHost, emp, Org);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/balance/{emp}/year-overview?year=2026"),
            "/api/balance/{employeeId}/year-overview", "get");

        var root = JsonDocument.Parse(body).RootElement;
        var categories = root.GetProperty("categories").EnumerateArray().ToList();
        Assert.Equal(4, categories.Count);
        foreach (var category in categories)
        {
            // THE ruling-#2 pin: the empty-config row carries the settlement KEY (null-valued).
            S118ContractAssert.AssertExactKeySet(category, CategoryKeys, "empty-config category row (ruling #2)");
            Assert.Equal(JsonValueKind.Null, category.GetProperty("settlement").ValueKind);

            // The rest of the empty shape — byte-identical to pre-S120.
            var saldo = category.GetProperty("saldo").EnumerateArray().ToList();
            Assert.Equal(12, saldo.Count);
            Assert.All(saldo, e => Assert.Equal(JsonValueKind.Null, e.ValueKind));
            var afholdt = category.GetProperty("afholdt").EnumerateArray().ToList();
            Assert.Equal(12, afholdt.Count);
            Assert.All(afholdt, e => Assert.Equal(0m, e.GetDecimal()));
            Assert.Equal(0m, category.GetProperty("expiring").GetDecimal());
            Assert.Equal(12, category.GetProperty("boundaryMonth").GetInt32());
        }
    }

    /// <summary>The CONFIGURED rows (byte-faithful — NOT part of the delta) with the
    /// list-nested wrapper's BOTH branches (application #3): the settled closed ferieår 2021
    /// (the REAL YEAR_END settle) makes viewing 2022 serve the VACATION category's
    /// <c>settlement</c> POPULATED — the exact 7-key disposition, matcher-recursed through the
    /// wrapper, with <c>expiring</c> pinned to the recorded §34 bucket (0) — while the
    /// calendar categories' closed year 2022 is unsettled ⇒ <c>settlement</c> NULL in the SAME
    /// response. The served <c>today</c> is byte-pinned to the FixedTimeProvider date (the
    /// determinism seam).</summary>
    [Fact]
    public async Task YearOverview_ConfiguredRows_SettledClosedFerieaar_BothWrapperBranches_TodayPinned()
    {
        var emp = await SeedEmployeeAsync("yo1");
        await SeedBalanceAsync(emp, ClosedYear, used: 20.75m);
        await SettleYearEndAsync(emp, ClosedYear);

        using var employee = S120ContractAssert.EmployeeClient(_fixedHost, emp, Org);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/balance/{emp}/year-overview?year=2022"),
            "/api/balance/{employeeId}/year-overview", "get");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("2026-03-05", root.GetProperty("today").GetString()); // the TimeProvider pin
        Assert.Equal(12, root.GetProperty("months").GetArrayLength());

        var categories = root.GetProperty("categories").EnumerateArray().ToList();
        Assert.Equal(4, categories.Count);
        foreach (var category in categories)
            S118ContractAssert.AssertExactKeySet(category, CategoryKeys, "configured category row");

        // VACATION: closed ferieår 2021 (reset 9 viewing 2022) — the settled row, POPULATED.
        var vacation = categories.Single(c => c.GetProperty("type").GetString() == VacationType);
        var settlement = vacation.GetProperty("settlement");
        Assert.Equal(JsonValueKind.Object, settlement.ValueKind); // wrapper #3 POPULATED branch, LIVE
        S118ContractAssert.AssertExactKeySet(settlement, DispositionKeys, "year-overview settled disposition");
        Assert.Equal("SETTLED", settlement.GetProperty("state").GetString());
        Assert.Equal(4.25m, settlement.GetProperty("payoutDays").GetDecimal());
        Assert.Equal(0m, settlement.GetProperty("forfeitDays").GetDecimal());
        Assert.False(settlement.GetProperty("forfeitPending").GetBoolean());
        // D9: expiring reads the RECORDED §34 bucket off the settled row (deterministic source).
        Assert.Equal(0m, vacation.GetProperty("expiring").GetDecimal());

        // CARE_DAY: closed year 2022 unsettled ⇒ the NULL branch in the SAME response.
        var careDay = categories.Single(c => c.GetProperty("type").GetString() == "CARE_DAY");
        Assert.Equal(JsonValueKind.Null, careDay.GetProperty("settlement").ValueKind);
    }

    // ─────────────────────────────── real-machinery drives ───────────────────────────────

    /// <summary>One YEAR_END settlement pass in its OWN ReadCommitted tx, committed — the exact
    /// SettlementCloseService shape (the S117SettlementSpecRuntimeTests drive; the REAL settle
    /// machinery, never SQL-faked). Fails loud unless the pass produced a SETTLED row.</summary>
    private async Task SettleYearEndAsync(string employeeId, int year)
    {
        var service = _fixedHost.Services.GetRequiredService<VacationSettlementService>();
        await using var conn = _fixedHost.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var outcome = await service.SettleAsync(employeeId, VacationType, year, "YEAR_END", conn, tx);
            await tx.CommitAsync();
            if (!outcome.DidSettle || outcome.Row is null
                || !string.Equals(outcome.Row.SettlementState, "SETTLED", StringComparison.Ordinal))
                throw new XunitException(
                    $"The YEAR_END settle drive for {employeeId}/{year} must produce a SETTLED row " +
                    $"(got DidSettle={outcome.DidSettle}, state={outcome.Row?.SettlementState}).");
        }
        catch
        {
            if (tx.Connection is not null)
                await tx.RollbackAsync();
            throw;
        }
    }

    // ─────────────────────────────── input-data seeds (NOT settlement states) ───────────────────────────────

    private async Task<string> SeedEmployeeAsync(string suffix)
    {
        var employeeId = "s120b_" + suffix;
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, Org, "AC", "OK24");
        return employeeId;
    }

    /// <summary>Consumption INPUT data (the closed year's <c>used</c>) — the operand the legal
    /// partition reads; the settlement STATE itself is always produced by the real settle pass
    /// (mirrors the S117/VacationSettlementServiceTests balance seed).</summary>
    private async Task SeedBalanceAsync(string employeeId, int year, decimal used)
    {
        await S120ContractAssert.ExecAsync(_harness.ConnectionString,
            """
            INSERT INTO entitlement_balances
                (balance_id, employee_id, entitlement_type, entitlement_year,
                 total_quota, used, planned, carryover_in, updated_at)
            VALUES (gen_random_uuid(), @e, @t, @y, 25, @used, 0, 0, NOW())
            ON CONFLICT (employee_id, entitlement_type, entitlement_year)
                DO UPDATE SET used = EXCLUDED.used, updated_at = NOW()
            """,
            ("e", employeeId), ("t", VacationType), ("y", year), ("used", used));
    }

    /// <summary>An <c>overtime_balances</c> INPUT row (model AFSPADSERING).</summary>
    private async Task SeedOvertimeBalanceAsync(
        string employeeId, int year, decimal accumulated, decimal paidOut, decimal afspadseringUsed)
    {
        await S120ContractAssert.ExecAsync(_harness.ConnectionString,
            """
            INSERT INTO overtime_balances
                (balance_id, employee_id, agreement_code, period_year,
                 accumulated, paid_out, afspadsering_used, compensation_model, updated_at)
            VALUES (gen_random_uuid(), @e, 'AC', @y, @acc, @po, @au, 'AFSPADSERING', NOW())
            """,
            ("e", employeeId), ("y", year), ("acc", accumulated), ("po", paidOut), ("au", afspadseringUsed));
    }

    /// <summary>The RULING-#2 absent-state fixture: a bare users row ONLY (no employee_profiles,
    /// no user_agreement_codes) under a fictitious agreement code with no entitlement_configs —
    /// the S65 graceful-branch reachable representative. MUST run after the last host boot so
    /// the EmployeeProfileSeeder cannot backfill it (the S63 boot-order lesson).</summary>
    private async Task InsertBareUserAsync(string userId, string agreementCode)
    {
        // Fresh container per test — the S120BAL1 org FK parent must be ensured here too.
        await S120ContractAssert.ExecAsync(_harness.ConnectionString,
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id,
                                       materialized_path, agreement_code, ok_version)
            VALUES (@org, 'S120BAL1 Test Org', 'ORGANISATION', NULL, '/S120BAL1/', 'AC', 'OK24')
            ON CONFLICT (org_id) DO NOTHING
            """, ("org", Org));
        await S120ContractAssert.ExecAsync(_harness.ConnectionString,
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@u, @u, 'dev-only', 'S120 Ruling2 Bare User', NULL, @org, @ac, 'OK24', TRUE)
            ON CONFLICT (user_id) DO NOTHING
            """,
            ("u", userId), ("org", Org), ("ac", agreementCode));
    }
}
