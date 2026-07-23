using System.Net.Http;
using System.Text.Json;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S120 / TASK-12002 — the per-route spec≡runtime gate for the SKEMA family drained in
/// retrofit Pass 7 (TASK-12000): the month GET (THE program's LARGEST composite — the FULL
/// 17-top-level-member tree asserted), the save POST (<c>{saved}</c> — the event-sourced
/// write's receipt, P3-fenced), and the row-preferences PUT (the SHARED
/// <c>SkemaRowPreferencesResponse</c> record — one shape, two surfaces).
///
/// <para><b>The 4th allOf wrapper member (<c>skemaMonth.approval</c>) BOTH branches:</b> NULL
/// when no approval period exists for the month, and POPULATED after a REAL create+submit
/// through <c>POST /api/approval/submit</c> — the matcher recurses THROUGH the wrapper into
/// the 6-member <c>SkemaApprovalInfo</c> (inner required + the 5-state <c>status</c> enum on
/// the live "SUBMITTED").</para>
///
/// <para><b>Per-op policy pins:</b> month GET + save POST are EmployeeOrAbove, driven at the
/// Employee floor (positive pins); the row-preferences PUT is SELF-ONLY BY DESIGN (S72
/// Step-5a B3) — a non-self employee AND an elevated GlobalAdmin are BOTH pinned 403 (the
/// covering-scope branch is deliberately absent on this write).</para>
///
/// <para><b>UNCONDITIONED pins (Step-0b Reviewer N2):</b> the save POST and row-preferences
/// PUT (with the choice PUT in the overtime class, the pass's unconditioned-mutation set) are
/// sent with NO If-Match/If-None-Match, succeed, and serve NO ETag.</para>
///
/// <para><b>Seed discipline (FAIL-002):</b> a FRESH testcontainer per test; org
/// <c>S120SKM1</c>, employees <c>s120s_*</c> (canonical <see cref="RegressionSeed"/> triple —
/// the dailyNorm/consumptionBasis seams resolve the dated AC profile). The named skema
/// tripwire suites (<c>tests/.../Skema/</c>) are UNMODIFIED — this class only ADDS coverage.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S120SkemaSpecRuntimeTests : IAsyncLifetime
{
    private const string Org = "S120SKM1";

    /// <summary>The EXACT 17 top-level members of <c>SkemaMonthResponse</c> — THE composite.</summary>
    private static readonly string[] MonthKeys =
    {
        "year", "month", "daysInMonth", "projects", "absenceTypes", "entries", "absences",
        "workTime", "dailyNorm", "approval", "employeeDeadline", "managerDeadline",
        "rowPreferences", "catalogs", "boundaryWorkTime", "fullDayNormAtMonthEnd", "consumptionBasis",
    };

    /// <summary>The EXACT 6 members of <c>SkemaApprovalInfo</c> (the wrapper's 4th member).</summary>
    private static readonly string[] ApprovalKeys =
        { "periodId", "status", "employeeDeadline", "managerDeadline", "employeeApprovedAt", "rejectionReason" };

    /// <summary>The EXACT 3 members of the SHARED <c>SkemaRowPreferencesResponse</c> (month GET
    /// member AND the PUT 200 body — the sibling rule).</summary>
    private static readonly string[] RowPreferencesKeys = { "configured", "projects", "absenceTypes" };

    /// <summary>The EXACT 2 members of <c>SkemaCatalogs</c>.</summary>
    private static readonly string[] CatalogsKeys = { "projects", "absenceTypes" };

    /// <summary>The EXACT 3 members of a <c>SkemaAbsenceTypeRow</c> (legacy field + catalogs —
    /// one computation, two projections).</summary>
    private static readonly string[] AbsenceTypeRowKeys = { "type", "label", "fullDayOnly" };

    /// <summary>The EXACT 2 members of a <c>SkemaDayHoursRow</c> (dailyNorm AND consumptionBasis
    /// — ONE record, sibling rule).</summary>
    private static readonly string[] DayHoursRowKeys = { "date", "hours" };

    /// <summary>The EXACT 3 members of a <c>SkemaWorkTimeDayRow</c>.</summary>
    private static readonly string[] WorkTimeDayKeys = { "date", "intervals", "manualHours" };

    /// <summary>The EXACT 1 member of <c>SkemaSaveResponse</c>.</summary>
    private static readonly string[] SaveKeys = { "saved" };

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
    //  Op 1 — GET /api/skema/{employeeId}/month — THE composite; approval NULL branch.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>THE composite through the matcher with the FULL top-level tree asserted: the
    /// exact 17-member key set; the per-day arrays at month length (31 rows for March, the
    /// dailyNorm/consumptionBasis sibling shape, weekday 7.4 / weekend 0 for the full-time AC
    /// profile); the shared 3-member absence-type row on BOTH its projections (legacy field +
    /// catalogs — the S72-B1 anti-drift identity); the R4 rowPreferences container
    /// (container-less ⇒ <c>configured: false</c>); the deadline scalars (monthEnd +2/+5); the
    /// R10 <c>fullDayNormAtMonthEnd</c>; and <c>approval</c> served NULL (the 4th wrapper
    /// member's null branch — no period exists).</summary>
    [Fact]
    public async Task Month_Get200_FullCompositeTree_ApprovalNullBranch()
    {
        var emp = await SeedEmployeeAsync("mon1");
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/skema/{emp}/month?year=2026&month=3"),
            "/api/skema/{employeeId}/month", "get");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, MonthKeys, "skema month composite");
        Assert.Equal(2026, root.GetProperty("year").GetInt32());
        Assert.Equal(3, root.GetProperty("month").GetInt32());
        Assert.Equal(31, root.GetProperty("daysInMonth").GetInt32());

        // The 4th wrapper member — the NULL branch (no approval period for the month).
        Assert.Equal(JsonValueKind.Null, root.GetProperty("approval").ValueKind);

        // Registrations: all empty for the fresh employee (fresh-container truth).
        Assert.Equal(0, root.GetProperty("entries").GetArrayLength());
        Assert.Equal(0, root.GetProperty("absences").GetArrayLength());
        Assert.Equal(0, root.GetProperty("workTime").GetArrayLength());
        Assert.Equal(0, root.GetProperty("boundaryWorkTime").GetArrayLength());
        Assert.Equal(0, root.GetProperty("projects").GetArrayLength()); // no org projects seeded

        // dailyNorm + consumptionBasis — ONE record, 31 rows each, weekday 7.4 / weekend 0
        // (AC 37h/5 × fraction 1.0; 2026-03-02 is a Monday, 2026-03-01 a Sunday).
        foreach (var member in new[] { "dailyNorm", "consumptionBasis" })
        {
            var rows = root.GetProperty(member).EnumerateArray().ToList();
            Assert.Equal(31, rows.Count);
            foreach (var row in rows)
                S118ContractAssert.AssertExactKeySet(row, DayHoursRowKeys, $"{member} row");
            Assert.Equal(7.4m, FindDay(rows, "2026-03-02").GetProperty("hours").GetDecimal()); // Monday
            Assert.Equal(0m, FindDay(rows, "2026-03-01").GetProperty("hours").GetDecimal());   // Sunday
        }
        Assert.Equal(7.4m, root.GetProperty("fullDayNormAtMonthEnd").GetDecimal()); // the R10 scalar

        // The shared absence-type row on BOTH projections (one computation, two projections).
        var legacyTypes = root.GetProperty("absenceTypes").EnumerateArray().ToList();
        Assert.NotEmpty(legacyTypes);
        foreach (var row in legacyTypes)
            S118ContractAssert.AssertExactKeySet(row, AbsenceTypeRowKeys, "legacy absenceTypes row");
        var catalogs = root.GetProperty("catalogs");
        S118ContractAssert.AssertExactKeySet(catalogs, CatalogsKeys, "skema catalogs");
        var catalogTypes = catalogs.GetProperty("absenceTypes").EnumerateArray().ToList();
        Assert.Equal(
            legacyTypes.Select(t => t.GetProperty("type").GetString()).ToList(),
            catalogTypes.Select(t => t.GetProperty("type").GetString()).ToList()); // identical set + order

        // The R4 container — container-less on a fresh employee.
        var rowPreferences = root.GetProperty("rowPreferences");
        S118ContractAssert.AssertExactKeySet(rowPreferences, RowPreferencesKeys, "rowPreferences (month GET surface)");
        Assert.False(rowPreferences.GetProperty("configured").GetBoolean());

        // The deadline scalars (monthEnd + 2 / + 5).
        Assert.Equal("2026-04-02", root.GetProperty("employeeDeadline").GetString());
        Assert.Equal("2026-04-05", root.GetProperty("managerDeadline").GetString());
    }

    /// <summary>The 4th wrapper member's POPULATED branch via REAL choreography: the employee
    /// create+submits the March approval period through <c>POST /api/approval/submit</c>, and
    /// the month GET then serves <c>approval</c> as the exact 6-member <c>SkemaApprovalInfo</c>
    /// — matcher-recursed through the wrapper, the 5-state <c>status</c> enum exercised on the
    /// live "SUBMITTED".</summary>
    [Fact]
    public async Task Month_Get200_ApprovalPopulated_ThroughTheWrapper_AfterRealSubmit()
    {
        var emp = await SeedEmployeeAsync("mon2");
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);

        // The REAL create+submit (EmployeeOrAbove; self-submit).
        using (var submit = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/approval/submit",
            $$"""
            { "employeeId": "{{emp}}", "orgId": "{{Org}}", "periodStart": "2026-03-01",
              "periodEnd": "2026-03-31", "periodType": "MONTHLY", "agreementCode": "AC", "okVersion": "OK24" }
            """)))
        {
            var submitBody = await submit.Content.ReadAsStringAsync();
            if ((int)submit.StatusCode != 200)
                throw new XunitException($"Approval submit for {emp} returned {(int)submit.StatusCode}: {submitBody}");
        }

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/skema/{emp}/month?year=2026&month=3"),
            "/api/skema/{employeeId}/month", "get");

        var approval = JsonDocument.Parse(body).RootElement.GetProperty("approval");
        Assert.Equal(JsonValueKind.Object, approval.ValueKind); // the POPULATED wrapper branch, LIVE
        S118ContractAssert.AssertExactKeySet(approval, ApprovalKeys, "skema approval (populated)");
        Assert.NotEqual(Guid.Empty, approval.GetProperty("periodId").GetGuid());
        Assert.Equal("SUBMITTED", approval.GetProperty("status").GetString()); // in the declared 5-state set
        Assert.Equal(JsonValueKind.Null, approval.GetProperty("employeeApprovedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, approval.GetProperty("rejectionReason").ValueKind);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 2 — POST /api/skema/{employeeId}/save — {saved}; UNCONDITIONED; P3-fenced.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The event-sourced batch write's receipt: a one-day work-time save (the
    /// rule-engine-free save lane) serves the exact 1-member <c>{saved: 1}</c> through the
    /// matcher, sent WITHOUT any precondition header (unconditioned mutation #3) and serving NO
    /// ETag; the month GET re-read serves the just-written row (the ADR-018 D12 read-your-write
    /// choreography through the in-tx projection) — the save's 17-site error fan stays untyped
    /// and untouched (S120 Explicit exclusions).</summary>
    [Fact]
    public async Task Save_Post200_SavedCountReceipt_Unconditioned_ThenMonthServesTheRow()
    {
        var emp = await SeedEmployeeAsync("sav1");
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);

        using var response = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/skema/{emp}/save",
            """
            { "year": 2026, "month": 3,
              "workTime": [ { "date": "2026-03-03", "intervals": [ { "start": "08:00", "end": "12:00" } ], "manualHours": 1.5 } ] }
            """)); // NO precondition header — the unconditioned pin
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)response.StatusCode);
        S120ContractAssert.AssertUnconditioned(response, "skema save POST 200");

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/skema/{employeeId}/save", "post");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "POST /api/skema/{employeeId}/save (200)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, SaveKeys, "skema save receipt");
        Assert.Equal(1, root.GetProperty("saved").GetInt32()); // one work-time day emitted

        // Read-your-write: the month GET serves the just-written row.
        var monthBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/skema/{emp}/month?year=2026&month=3"),
            "/api/skema/{employeeId}/month", "get");
        var workTime = JsonDocument.Parse(monthBody).RootElement.GetProperty("workTime");
        var day = Assert.Single(workTime.EnumerateArray());
        S118ContractAssert.AssertExactKeySet(day, WorkTimeDayKeys, "workTime row");
        Assert.Equal("2026-03-03", day.GetProperty("date").GetString());
        Assert.Equal(1.5m, day.GetProperty("manualHours").GetDecimal());
        var interval = Assert.Single(day.GetProperty("intervals").EnumerateArray());
        Assert.Equal("08:00", interval.GetProperty("start").GetString());
        Assert.Equal("12:00", interval.GetProperty("end").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — PUT row-preferences — the SHARED record; SELF-ONLY; UNCONDITIONED.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The R4 full-replacement write on the SHARED record (one shape, two surfaces):
    /// the employee-self PUT of the legal configured-empty state succeeds WITHOUT any
    /// precondition header and serves NO ETag; the 200 body is the exact 3-member container
    /// (<c>configured: true</c>, both sets empty) through the matcher; the month GET re-read
    /// serves the SAME record shape with the container now authoritative-even-empty, while the
    /// legacy <c>absenceTypes</c> field stays the selection-INDEPENDENT catalog (B1's
    /// authoritative-even-empty governs the VISIBLE sets; the legacy `projects` emptying is
    /// pinned by the unmodified skema tripwire suites, not here — this fixture seeds no org
    /// projects, so an assertion would be vacuous). <b>The SELF-ONLY pins:</b> a NON-SELF employee
    /// AND an elevated GlobalAdmin are BOTH 403 — the covering-scope branch is deliberately
    /// absent on this write (S72 Step-5a B3; the mixed-role-JWT hole stays closed).</summary>
    [Fact]
    public async Task RowPreferences_Put200_SharedRecordBothSurfaces_SelfOnlyPins_Unconditioned()
    {
        var emp = await SeedEmployeeAsync("pref1");
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);

        using var response = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/skema/{emp}/row-preferences",
            """{ "projects": [], "absenceTypes": [] }""")); // NO precondition header — the unconditioned pin
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)response.StatusCode);
        S120ContractAssert.AssertUnconditioned(response, "row-preferences PUT 200");

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/skema/{employeeId}/row-preferences", "put");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "PUT .../row-preferences (200)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, RowPreferencesKeys, "row-preferences PUT 200 (surface 2)");
        Assert.True(root.GetProperty("configured").GetBoolean()); // the PUT always serves configured: true
        Assert.Equal(0, root.GetProperty("projects").GetArrayLength());
        Assert.Equal(0, root.GetProperty("absenceTypes").GetArrayLength());

        // Surface 1 — the month GET's rowPreferences member is the SAME record, now configured.
        var monthBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/skema/{emp}/month?year=2026&month=3"),
            "/api/skema/{employeeId}/month", "get");
        var monthRoot = JsonDocument.Parse(monthBody).RootElement;
        var rowPreferences = monthRoot.GetProperty("rowPreferences");
        S118ContractAssert.AssertExactKeySet(rowPreferences, RowPreferencesKeys, "rowPreferences (surface 1 re-read)");
        Assert.True(rowPreferences.GetProperty("configured").GetBoolean());
        Assert.Equal(0, rowPreferences.GetProperty("absenceTypes").GetArrayLength());
        // The legacy top-level `absenceTypes` is the CATALOG projection (selection-
        // INDEPENDENT — SkemaEndpoints "one computation, two projections"): it must NOT
        // empty when the configured container does. B1's authoritative-even-empty applies
        // to the VISIBLE sets (rowPreferences.absenceTypes above + the legacy `projects`).
        var legacyAbsenceTypes = monthRoot.GetProperty("absenceTypes");
        var catalogAbsenceTypes = monthRoot.GetProperty("catalogs").GetProperty("absenceTypes");
        Assert.True(legacyAbsenceTypes.GetArrayLength() > 0); // the agreement catalog survives
        Assert.Equal(catalogAbsenceTypes.GetArrayLength(), legacyAbsenceTypes.GetArrayLength());

        // The SELF-ONLY pins: non-self employee 403 AND elevated GlobalAdmin 403.
        var alien = await SeedEmployeeAsync("pref1b");
        using var alienClient = S120ContractAssert.EmployeeClient(_factory, alien, Org);
        using (var denied = await alienClient.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/skema/{emp}/row-preferences",
            """{ "projects": [], "absenceTypes": [] }""")))
            Assert.Equal(403, (int)denied.StatusCode);

        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s120s_gadmin", Org);
        using (var deniedAdmin = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/skema/{emp}/row-preferences",
            """{ "projects": [], "absenceTypes": [] }""")))
            Assert.Equal(403, (int)deniedAdmin.StatusCode); // self-only even for GLOBAL scope
    }

    // ─────────────────────────────── seeds / helpers ───────────────────────────────

    private async Task<string> SeedEmployeeAsync(string suffix)
    {
        var employeeId = "s120s_" + suffix;
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, Org, "AC", "OK24");
        return employeeId;
    }

    private static JsonElement FindDay(IReadOnlyList<JsonElement> rows, string date)
    {
        foreach (var row in rows)
            if (string.Equals(row.GetProperty("date").GetString(), date, StringComparison.Ordinal))
                return row;
        throw new XunitException($"Expected a row for {date}.");
    }
}
