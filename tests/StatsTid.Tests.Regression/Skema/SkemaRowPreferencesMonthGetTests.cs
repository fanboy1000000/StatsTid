using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S72 / TASK-7201 — GET-shape pins for the month GET's four ADDITIVE fields (SPRINT-72 R4 +
/// R10): <c>rowPreferences</c> (catalog ∩ selections when the container exists; today's
/// fallback when it does not), <c>catalogs</c> (selection-INDEPENDENT addable sets),
/// <c>feriedage</c> on each served absence row (nullable passthrough), <c>boundaryWorkTime</c>
/// (EXACTLY the prev-month last day + next-month first day), and the
/// <c>fullDayNormAtMonthEnd</c> scalar (weekday formula, weekend-placement-independent,
/// fail-soft null). Fresh org per test (full control of the project catalog); preference
/// state is seeded DIRECTLY (DB rows) so these pins exercise the GET independent of the PUT.
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaRowPreferencesMonthGetTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Boot the host ONCE here (Program.cs seeders run now). Tests create their users
        // AFTER this boot, so the S31 EmployeeProfileSeeder never backfills profile rows
        // for them (the S63 boot-order lesson) — the no-profile test below depends on it.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ───────────────────── rowPreferences / catalogs (R4) ─────────────────────

    /// <summary>
    /// Container present + a selection SUBSET: visible = catalog ∩ selections in the
    /// per-user order; the catalogs stay FULL (selection-independent — removed rows remain
    /// addable). The legacy `projects` field serves the SAME visible set (container exists ⇒
    /// selections authoritative, Step-5a B1) — catalog ≠ visible is the R4 headline.
    /// </summary>
    [Fact]
    public async Task ContainerPresent_SelectionSubset_VisibleIsSubset_CatalogsStayFull()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        // Org catalog: 3 projects. Per-user selection: C (order 0) + A (order 1); B removed.
        var projA = await InsertProjectAsync(orgId, "S72A", orgSortOrder: 10);
        var projB = await InsertProjectAsync(orgId, "S72B", orgSortOrder: 20);
        var projC = await InsertProjectAsync(orgId, "S72C", orgSortOrder: 30);
        await InsertContainerAsync(emp);
        await InsertProjectSelectionAsync(emp, projC, sortOrder: 0);
        await InsertProjectSelectionAsync(emp, projA, sortOrder: 1);
        // Absence rows: VACATION first, SICK_DAY second — a subset of the catalog.
        await InsertAbsenceSelectionAsync(emp, "VACATION", sortOrder: 0);
        await InsertAbsenceSelectionAsync(emp, "SICK_DAY", sortOrder: 1);

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        var prefs = body.GetProperty("rowPreferences");
        Assert.True(prefs.GetProperty("configured").GetBoolean());

        // Visible projects: the user's order (C then A), dense sortOrder 0..1 — NOT the
        // org order, and NOT the full catalog.
        var visible = prefs.GetProperty("projects").EnumerateArray().ToList();
        Assert.Equal(2, visible.Count);
        Assert.Equal(projC, visible[0].GetProperty("projectId").GetGuid());
        Assert.Equal(0, visible[0].GetProperty("sortOrder").GetInt32());
        Assert.Equal("S72C", visible[0].GetProperty("projectCode").GetString());
        Assert.Equal(projA, visible[1].GetProperty("projectId").GetGuid());
        Assert.Equal(1, visible[1].GetProperty("sortOrder").GetInt32());

        // Catalog projects: ALL three, in org order (sort_order, code) — selection-independent.
        var catalogProjects = body.GetProperty("catalogs").GetProperty("projects").EnumerateArray().ToList();
        Assert.Equal(new[] { projA, projB, projC },
            catalogProjects.Select(p => p.GetProperty("projectId").GetGuid()).ToArray());

        // Visible absence rows: the selected pair in selection order, dense sortOrder.
        var visibleAbs = prefs.GetProperty("absenceTypes").EnumerateArray().ToList();
        Assert.Equal(new[] { "VACATION", "SICK_DAY" },
            visibleAbs.Select(a => a.GetProperty("type").GetString()).ToArray());
        Assert.Equal(new[] { 0, 1 },
            visibleAbs.Select(a => a.GetProperty("sortOrder").GetInt32()).ToArray());
        Assert.Equal("Ferie", visibleAbs[0].GetProperty("label").GetString());

        // Catalog absence types: byte-identical (type+order) to the EXISTING absenceTypes
        // field — one shared chain, two projections.
        var existingTypes = body.GetProperty("absenceTypes").EnumerateArray()
            .Select(a => a.GetProperty("type").GetString()).ToArray();
        var catalogTypes = body.GetProperty("catalogs").GetProperty("absenceTypes").EnumerateArray()
            .Select(a => a.GetProperty("type").GetString()).ToArray();
        Assert.Equal(existingTypes, catalogTypes);
        Assert.True(catalogTypes.Length > 2); // the catalog is bigger than the visible subset

        // The legacy `projects` field: container exists ⇒ the SAME visible set as
        // rowPreferences.projects (Step-5a B1), in the per-user order.
        var legacyProjects = body.GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("projectId").GetGuid()).ToArray();
        Assert.Equal(new[] { projC, projA }, legacyProjects);
    }

    /// <summary>
    /// EMPTY-but-configured (the R4 even-when-empty rule, corrected per Step-5a B1): a
    /// container with ZERO selection rows serves ZERO visible rows — and the legacy
    /// `projects` field is ALSO empty (container exists ⇒ selections authoritative on
    /// EVERY row-serving read path; the all-org fallback applies ONLY to container-less
    /// users) — while BOTH catalogs stay full (the rows remain re-addable).
    /// </summary>
    [Fact]
    public async Task EmptyButConfigured_ServesZeroVisibleRows_AndFullCatalogs()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", orgSortOrder: 10);
        await InsertContainerAsync(emp); // configured, zero selections

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        var prefs = body.GetProperty("rowPreferences");
        Assert.True(prefs.GetProperty("configured").GetBoolean());
        Assert.Empty(prefs.GetProperty("projects").EnumerateArray());
        Assert.Empty(prefs.GetProperty("absenceTypes").EnumerateArray());

        // Catalogs full.
        Assert.Equal(projA, body.GetProperty("catalogs").GetProperty("projects")
            .EnumerateArray().Single().GetProperty("projectId").GetGuid());
        Assert.NotEmpty(body.GetProperty("catalogs").GetProperty("absenceTypes").EnumerateArray());

        // Legacy `projects` field: the container EXISTS ⇒ the same (EMPTY) visible set —
        // no all-org fallback (Step-5a B1; pre-fix this served ALL org projects).
        Assert.Empty(body.GetProperty("projects").EnumerateArray());
    }

    /// <summary>
    /// Step-5a B1, the stale-only shape: a CONFIGURED user whose every selection is stale
    /// (a deactivated project) serves an EMPTY visible set AND an empty legacy `projects`
    /// field — stale selections neither resurrect NOR re-trigger the all-org fallback —
    /// while the catalog stays intact (the org's ACTIVE projects remain addable).
    /// </summary>
    [Fact]
    public async Task ConfiguredWithStaleOnlySelections_LegacyFieldEmpty_CatalogIntact()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projLive = await InsertProjectAsync(orgId, "S72L", orgSortOrder: 10); // unselected
        var projDead = await InsertProjectAsync(orgId, "S72D", orgSortOrder: 20);
        await ExecAsync("UPDATE projects SET is_active = FALSE WHERE project_id = @p0", projDead);

        await InsertContainerAsync(emp);
        await InsertProjectSelectionAsync(emp, projDead, sortOrder: 0); // the ONLY selection — stale

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        // Visible: empty (catalog ∩ selections = ∅ — the JOIN filters is_active).
        var prefs = body.GetProperty("rowPreferences");
        Assert.True(prefs.GetProperty("configured").GetBoolean());
        Assert.Empty(prefs.GetProperty("projects").EnumerateArray());

        // Legacy field: ALSO empty — configured-with-only-stale-selections must not fall
        // back to all-org (pre-B1 it did: the selected-set JOIN came back empty, the
        // Count-based condition fired, and ALL org projects were served).
        Assert.Empty(body.GetProperty("projects").EnumerateArray());

        // Catalog intact: the live project is still addable.
        Assert.Equal(projLive, body.GetProperty("catalogs").GetProperty("projects")
            .EnumerateArray().Single().GetProperty("projectId").GetGuid());
    }

    /// <summary>
    /// Step-5a B2, the configured half: a CONFIGURED user's project order is FROZEN against
    /// a later admin reorder of the org-level <c>projects.sort_order</c> — both the
    /// <c>rowPreferences.projects</c> field and the legacy <c>projects</c> field keep the
    /// per-user order (<c>ups.sort_order, project_code</c>), which the org reorder never
    /// touches. (The container-less half — LIVE org ordering — is pinned in
    /// <c>SkemaRowPreferencesFallbackRegressionTests</c>.)
    /// </summary>
    [Fact]
    public async Task Configured_AdminReorderOfOrgSortOrder_PerUserOrderFrozen()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", orgSortOrder: 10);
        var projB = await InsertProjectAsync(orgId, "S72B", orgSortOrder: 20);
        await InsertContainerAsync(emp);
        await InsertProjectSelectionAsync(emp, projB, sortOrder: 0); // user's order: B then A
        await InsertProjectSelectionAsync(emp, projA, sortOrder: 1);

        // Admin reorders the org catalog (A last, B first — would flip a LIVE org read).
        await ExecAsync("UPDATE projects SET sort_order = 99 WHERE project_id = @p0", projA);
        await ExecAsync("UPDATE projects SET sort_order = 1 WHERE project_id = @p0", projB);

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        // The per-user order is untouched by the org reorder, on BOTH projections.
        var visible = body.GetProperty("rowPreferences").GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("projectId").GetGuid()).ToArray();
        Assert.Equal(new[] { projB, projA }, visible);
        var legacy = body.GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("projectId").GetGuid()).ToArray();
        Assert.Equal(new[] { projB, projA }, legacy);
    }

    /// <summary>
    /// Container-less (today's fallback): visible == catalog for BOTH row families, and the
    /// visible absence list mirrors the existing `absenceTypes` field exactly.
    /// </summary>
    [Fact]
    public async Task NoContainer_VisibleEqualsCatalog_TodaysFallback()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", orgSortOrder: 10);
        var projB = await InsertProjectAsync(orgId, "S72B", orgSortOrder: 20);

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        var prefs = body.GetProperty("rowPreferences");
        Assert.False(prefs.GetProperty("configured").GetBoolean());

        var visibleIds = prefs.GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("projectId").GetGuid()).ToArray();
        var catalogIds = body.GetProperty("catalogs").GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("projectId").GetGuid()).ToArray();
        Assert.Equal(new[] { projA, projB }, visibleIds);
        Assert.Equal(catalogIds, visibleIds);

        var visibleTypes = prefs.GetProperty("absenceTypes").EnumerateArray()
            .Select(a => a.GetProperty("type").GetString()).ToArray();
        var existingTypes = body.GetProperty("absenceTypes").EnumerateArray()
            .Select(a => a.GetProperty("type").GetString()).ToArray();
        Assert.Equal(existingTypes, visibleTypes);
    }

    /// <summary>
    /// Stale selections never resurrect (R4: visible = catalog ∩ selections): a selection
    /// row for a DEACTIVATED project and one for an ORG-HIDDEN absence type are both
    /// filtered out of the visible sets — and out of the catalogs too.
    /// </summary>
    [Fact]
    public async Task StaleSelections_InactiveProjectAndOrgHiddenType_NeverResurrect()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projLive = await InsertProjectAsync(orgId, "S72L", orgSortOrder: 10);
        var projDead = await InsertProjectAsync(orgId, "S72D", orgSortOrder: 20);
        await ExecAsync("UPDATE projects SET is_active = FALSE WHERE project_id = @p0", projDead);
        await HideAbsenceTypeAsync(orgId, "CARE_DAY");

        await InsertContainerAsync(emp);
        await InsertProjectSelectionAsync(emp, projDead, sortOrder: 0); // stale
        await InsertProjectSelectionAsync(emp, projLive, sortOrder: 1);
        await InsertAbsenceSelectionAsync(emp, "CARE_DAY", sortOrder: 0); // stale (org-hidden)
        await InsertAbsenceSelectionAsync(emp, "VACATION", sortOrder: 1);

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        var prefs = body.GetProperty("rowPreferences");
        Assert.Equal(projLive, prefs.GetProperty("projects")
            .EnumerateArray().Single().GetProperty("projectId").GetGuid());
        Assert.Equal("VACATION", prefs.GetProperty("absenceTypes")
            .EnumerateArray().Single().GetProperty("type").GetString());

        // The catalogs exclude them too (inactive ∉ org read; hidden ∉ filter chain).
        Assert.DoesNotContain(projDead,
            body.GetProperty("catalogs").GetProperty("projects").EnumerateArray()
                .Select(p => p.GetProperty("projectId").GetGuid()));
        Assert.DoesNotContain("CARE_DAY",
            body.GetProperty("catalogs").GetProperty("absenceTypes").EnumerateArray()
                .Select(a => a.GetProperty("type").GetString()));
    }

    // ───────────────────── feriedage passthrough (R10) ─────────────────────

    /// <summary>
    /// Each served absence row carries the recorded ADR-032 <c>feriedage</c> verbatim —
    /// including the NULL passthrough (ADR-032 persists null on zero-norm days /
    /// non-entitlement rows; the FE's day sub-lines skip null-valued rows, R10/N4).
    /// </summary>
    [Fact]
    public async Task Absences_ServeRecordedFeriedage_IncludingNullPassthrough()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        await InsertAbsenceProjectionRowAsync(emp, new DateOnly(2025, 5, 5), "VACATION", 7.4m, feriedage: 1.0m, outboxId: 1);
        await InsertAbsenceProjectionRowAsync(emp, new DateOnly(2025, 5, 6), "SICK_DAY", 7.4m, feriedage: null, outboxId: 2);

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        var absences = body.GetProperty("absences").EnumerateArray().ToList();
        Assert.Equal(2, absences.Count);
        Assert.Equal("VACATION", absences[0].GetProperty("absenceType").GetString());
        Assert.Equal(1.0m, absences[0].GetProperty("feriedage").GetDecimal());
        Assert.Equal(7.4m, absences[0].GetProperty("hours").GetDecimal()); // existing fields intact
        Assert.Equal("SICK_DAY", absences[1].GetProperty("absenceType").GetString());
        Assert.Equal(JsonValueKind.Null, absences[1].GetProperty("feriedage").ValueKind);
    }

    // ───────────────────── boundaryWorkTime ─────────────────────

    /// <summary>
    /// EXACTLY two extra days are served — the previous month's LAST day and the next
    /// month's FIRST day. Registrations one day further out (noise) must NOT leak in, and
    /// the in-month <c>workTime</c> array must not absorb the boundary rows.
    /// </summary>
    [Fact]
    public async Task BoundaryWorkTime_ExactlyTwoDays_NoiseExcluded()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        // Viewing 2025-05: boundaries are 2025-04-30 and 2025-06-01.
        await InsertWorkTimeRowAsync(emp, new DateOnly(2025, 4, 29), "08:00", "16:00", 0m, outboxId: 1); // noise
        await InsertWorkTimeRowAsync(emp, new DateOnly(2025, 4, 30), "09:00", "17:00", 0.5m, outboxId: 2);
        await InsertWorkTimeRowAsync(emp, new DateOnly(2025, 6, 1), "10:00", "18:00", 0m, outboxId: 3);
        await InsertWorkTimeRowAsync(emp, new DateOnly(2025, 6, 2), "08:00", "12:00", 0m, outboxId: 4); // noise

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        var boundary = body.GetProperty("boundaryWorkTime").EnumerateArray().ToList();
        Assert.Equal(2, boundary.Count);
        Assert.Equal("2025-04-30", boundary[0].GetProperty("date").GetString());
        Assert.Equal(0.5m, boundary[0].GetProperty("manualHours").GetDecimal());
        var interval = boundary[0].GetProperty("intervals").EnumerateArray().Single();
        Assert.Equal("09:00", interval.GetProperty("start").GetString());
        Assert.Equal("17:00", interval.GetProperty("end").GetString());
        Assert.Equal("2025-06-01", boundary[1].GetProperty("date").GetString());

        // The month array stays month-scoped (no boundary rows absorbed).
        Assert.Empty(body.GetProperty("workTime").EnumerateArray());
    }

    /// <summary>January: the boundary days cross BOTH year edges (Dec 31 / Feb 1).</summary>
    [Fact]
    public async Task BoundaryWorkTime_January_CrossesYearBoundary()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        await InsertWorkTimeRowAsync(emp, new DateOnly(2025, 12, 31), "08:00", "16:00", 0m, outboxId: 1);
        await InsertWorkTimeRowAsync(emp, new DateOnly(2026, 2, 1), "10:00", "14:00", 0m, outboxId: 2);

        var body = await GetMonthAsync(emp, orgId, 2026, 1);

        var dates = body.GetProperty("boundaryWorkTime").EnumerateArray()
            .Select(w => w.GetProperty("date").GetString()).ToArray();
        Assert.Equal(new[] { "2025-12-31", "2026-02-01" }, dates);
    }

    // ───────────────────── fullDayNormAtMonthEnd (R10) ─────────────────────

    /// <summary>
    /// The scalar is INDEPENDENT of the month-end's weekend placement: July 2025 ends on a
    /// THURSDAY, August 2025 on a SUNDAY — both serve the same full-time weekday norm
    /// 37 × 1.0 ÷ 5 = 7.4 (the dailyNorm array meanwhile serves 0 for the Sunday, proving
    /// the scalar deliberately bypasses the weekend-0 rule).
    /// </summary>
    [Fact]
    public async Task FullDayNorm_WeekdayEndAndWeekendEndMonths_SameScalar()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);

        var july = await GetMonthAsync(emp, orgId, 2025, 7);   // 2025-07-31 = Thursday
        var august = await GetMonthAsync(emp, orgId, 2025, 8); // 2025-08-31 = Sunday

        Assert.Equal(7.4m, july.GetProperty("fullDayNormAtMonthEnd").GetDecimal());
        Assert.Equal(7.4m, august.GetProperty("fullDayNormAtMonthEnd").GetDecimal());

        // Contrast pin: the per-day norm for the weekend month-end day itself is 0.
        var aug31 = august.GetProperty("dailyNorm").EnumerateArray()
            .Single(n => n.GetProperty("date").GetString() == "2025-08-31");
        Assert.Equal(0m, aug31.GetProperty("hours").GetDecimal());
    }

    /// <summary>The part-time fraction scales the scalar: 37 × 0.5 ÷ 5 = 3.7.</summary>
    [Fact]
    public async Task FullDayNorm_PartTimeFraction_ScalesScalar()
    {
        var orgId = NewOrgId();
        var emp = "emp_s72_get_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, orgId, "AC", "OK24",
            partTimeFraction: 0.500m);

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        Assert.Equal(3.7m, body.GetProperty("fullDayNormAtMonthEnd").GetDecimal());
    }

    /// <summary>ANNUAL_ACTIVITY (academic, AC_RESEARCH) ⇒ null — a weekday split is not meaningful.</summary>
    [Fact]
    public async Task FullDayNorm_AnnualActivity_Null()
    {
        var orgId = NewOrgId();
        var emp = "emp_s72_get_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, orgId, "AC_RESEARCH", "OK24");

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        Assert.Equal(JsonValueKind.Null, body.GetProperty("fullDayNormAtMonthEnd").ValueKind);
    }

    /// <summary>
    /// No dated profile covering the month-end ⇒ null, FAIL-SOFT: the GET still serves 200
    /// (the FE em-dashes the hours headline). The user is created AFTER the host boot so the
    /// S31 profile seeder cannot have backfilled a row (the S63 boot-order lesson).
    /// </summary>
    [Fact]
    public async Task FullDayNorm_NoDatedProfile_NullAndStill200()
    {
        var orgId = NewOrgId();
        // Ensure the org exists (throwaway seeded employee), then create a BARE user with an
        // agreement-code row but NO employee_profiles row.
        await SeedEmployeeAsync(orgId);
        var emp = "emp_s72_get_" + Guid.NewGuid().ToString("N")[..8];
        await ExecAsync(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@p0, @p0, 'dev-only', 'S72 no-profile user', NULL, @p1, 'AC', 'OK24', TRUE)
            """, emp, orgId);
        await ExecAsync(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @p0, 'AC', '-infinity', NULL, 1)
            """, emp);

        var rsp = await GetMonthResponseAsync(emp, orgId, 2025, 5);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("fullDayNormAtMonthEnd").ValueKind);
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private static string NewOrgId() => "S72G" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<string> SeedEmployeeAsync(string orgId)
    {
        var employeeId = "emp_s72_get_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, orgId, "AC", "OK24");
        return employeeId;
    }

    private async Task<JsonElement> GetMonthAsync(string employeeId, string orgId, int year, int month)
    {
        var rsp = await GetMonthResponseAsync(employeeId, orgId, year, month);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> GetMonthResponseAsync(string employeeId, string orgId, int year, int month)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId, orgId));
        return await client.GetAsync($"/api/skema/{employeeId}/month?year={year}&month={month}");
    }

    private async Task<Guid> InsertProjectAsync(string orgId, string code, int orgSortOrder)
    {
        var result = await ScalarAsync(
            """
            INSERT INTO projects (org_id, project_code, project_name, sort_order, created_by)
            VALUES (@p0, @p1, @p2, @p3, 'test')
            RETURNING project_id
            """, orgId, code, "S72 GET test " + code, orgSortOrder);
        return (Guid)result!;
    }

    private Task InsertContainerAsync(string employeeId)
        => ExecAsync("INSERT INTO user_skema_preferences (employee_id) VALUES (@p0)", employeeId);

    private Task InsertProjectSelectionAsync(string employeeId, Guid projectId, int sortOrder)
        => ExecAsync(
            "INSERT INTO user_project_selections (employee_id, project_id, sort_order) VALUES (@p0, @p1, @p2)",
            employeeId, projectId, sortOrder);

    private Task InsertAbsenceSelectionAsync(string employeeId, string absenceType, int sortOrder)
        => ExecAsync(
            "INSERT INTO user_absence_selections (employee_id, absence_type, sort_order) VALUES (@p0, @p1, @p2)",
            employeeId, absenceType, sortOrder);

    private Task HideAbsenceTypeAsync(string orgId, string absenceType)
        => ExecAsync(
            "INSERT INTO absence_type_visibility (org_id, absence_type, is_hidden, set_by) VALUES (@p0, @p1, TRUE, 'test')",
            orgId, absenceType);

    private Task InsertAbsenceProjectionRowAsync(
        string employeeId, DateOnly date, string absenceType, decimal hours, decimal? feriedage, long outboxId)
        => ExecAsync(
            """
            INSERT INTO absences_projection (event_id, employee_id, date, absence_type, hours, feriedage,
                                             agreement_code, ok_version, occurred_at, outbox_id)
            VALUES (gen_random_uuid(), @p0, @p1, @p2, @p3, @p4, 'AC', 'OK24', NOW(), @p5)
            """, employeeId, date, absenceType, hours, (object?)feriedage ?? DBNull.Value, outboxId);

    private Task InsertWorkTimeRowAsync(
        string employeeId, DateOnly date, string start, string end, decimal manualHours, long outboxId)
        => ExecAsync(
            """
            INSERT INTO work_time_projection (employee_id, date, intervals, manual_hours, occurred_at, outbox_id)
            VALUES (@p0, @p1, @p2::jsonb, @p3, NOW(), @p4)
            """, employeeId, date, $$"""[{"start":"{{start}}","end":"{{end}}"}]""", manualHours, outboxId);

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return await cmd.ExecuteScalarAsync();
    }

    private static string MintEmployeeToken(string actorId, string orgId)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }
}
