using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// S141 / TASK-14106, wave 2/3 — pin 6: the AS-OF-TODAY reads that put an employee's position on a
/// screen OTHER than the profile page/edit drawer, with a scheduled future change present; PLUS the
/// owner's B0 marker (<c>scheduledChangeFrom</c>) on those same three surfaces, added by TASK-14116
/// (now merged) and pinned here per the sprint-end review's W3 finding — the only pins that existed
/// on the marker were string-contains checks against the SQL text, which pass whether or not the
/// query returns the right date.
///
/// <para>
/// <b>Why this matters, in plain language.</b> Before this sprint, an employee's position never
/// changed except by a today-dated edit, so any read of "the row with no end date" was always
/// today's truth. Once HR can schedule a promotion for a future date, a read that still follows the
/// OPEN row would show the roster (and the merged-admin overlay's people search) a job title that is
/// not yet in force — weeks early, with nothing on screen saying so. That half (position accuracy)
/// is TASK-14102's; the SECOND half — the marker itself, a date on screen saying "a change is coming"
/// — is TASK-14116's, and is what most of this file pins. The marker's own rule (owner-critical): a
/// CANCELLED scheduled change (a retired, zero-width row) must never be reported as scheduled — the
/// owner's requirement hinges on not announcing a change somebody already called off.
/// </para>
///
/// <para>
/// <b>Scope note, found while writing this file rather than assumed from the refinement's prose.</b>
/// The refinement names "the organisation roster, people search and the person-reference resolver"
/// as the three affected surfaces. Reading the actual endpoints (<c>AdminEndpoints.cs</c>) shows
/// this maps to exactly TWO HTTP surfaces: <c>GET
/// /api/admin/reporting-lines/tree/{organisationId}/medarbejdere</c> (the roster) carries the marker
/// on both its employee rows AND its <c>nameResolution</c> dictionary (the "person-reference
/// resolver" the refinement names is this same endpoint's cross-reference lookup, not a separate
/// route) — and <c>GET /api/admin/search</c> (the merged-admin overlay's people section, which is
/// what "people search" means here). <c>GET /api/admin/users/search</c> — a DIFFERENT, older
/// person-search endpoint — was deliberately left untouched by both TASK-14102 and TASK-14116 (per
/// their own doc comments) and never surfaced position or the marker on the wire at all, so it is out
/// of scope and not tested here. The <c>nameResolution</c> case (person-reference) IS now covered,
/// via a minimal second employee whose PRIMARY reporting line names the subject as manager — the
/// smallest fixture that gets the subject's id into the resolver's input set.
/// </para>
///
/// <para>
/// <b>One shared rule, tested through one timeline.</b> <c>EmploymentTimelineSql.EarliestScheduledChangeLateral</c>
/// takes the MIN effective date over BOTH the profile and the agreement-code timelines. This file
/// exercises the rule through the profile timeline only (matching this file's existing seeding); the
/// agreement-code half of the same MIN is not separately pinned here, to avoid combinatorial re-tests
/// of a rule this file already establishes is date-and-width based, not table-based — flagged in the
/// final report as a deliberate narrower cut.
/// </para>
///
/// <para>
/// <b>RED-FIRST, expected UNRUNNABLE.</b> Written from the spec; Docker is unavailable locally, so
/// nothing here is verified and nothing is reported as passing. TASK-14102 and TASK-14116 have both
/// merged, so it is plausible these pins are close to or at green, but that remains unverified here.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ScheduledChangeVisibilityTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _fixedHost = null!;

    private static readonly DateOnly F = new(2025, 3, 12);

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _fixedHost = _factory.WithFixedToday(F);
        _ = _fixedHost.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _fixedHost?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    [Fact]
    public void Anchor_IsWednesday_OnOk24Side()
    {
        Assert.Equal(DayOfWeek.Wednesday, F.DayOfWeek);
        Assert.Equal("OK24", OkVersionResolver.ResolveVersion(F));
    }

    /// <summary>
    /// The organisation roster must show TODAY's position, not a promotion scheduled weeks ahead.
    /// </summary>
    [Fact]
    public async Task Roster_WithScheduledPositionChangePresent_ShowsTodaysPosition()
    {
        var employeeId = await SeedEmployeeAsync("Roster Vis Test");
        var scheduledFrom = F.AddDays(21);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), scheduledFrom, "Specialkonsulent"),
            (scheduledFrom, null, "Kontorchef"));

        var client = AdminClient();
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{OrgId}/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        var matches = body.GetProperty("employees").EnumerateArray()
            .Where(e => e.GetProperty("employeeId").GetString() == employeeId)
            .ToList();
        var row = Assert.Single(matches);

        // RED: fails if the roster still follows the OPEN row (it would read "Kontorchef", the
        // promotion that has not taken effect, weeks early) instead of the row covering TODAY.
        Assert.Equal("Specialkonsulent", row.GetProperty("position").GetString());
    }

    /// <summary>
    /// The merged-admin overlay's people search must show TODAY's position for the same reason.
    /// </summary>
    [Fact]
    public async Task PersonSearchOverlay_WithScheduledPositionChangePresent_ShowsTodaysPosition()
    {
        var employeeId = await SeedEmployeeAsync("Overlay Vis Test Zzq");
        var scheduledFrom = F.AddDays(21);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), scheduledFrom, "Specialkonsulent"),
            (scheduledFrom, null, "Kontorchef"));

        var client = AdminClient();
        var rsp = await client.GetAsync("/api/admin/search?q=Overlay+Vis+Test+Zzq");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        var matches = body.GetProperty("people").EnumerateArray()
            .Where(p => p.GetProperty("userId").GetString() == employeeId)
            .ToList();
        var row = Assert.Single(matches);

        // RED: fails if the overlay search still follows the OPEN row.
        Assert.Equal("Specialkonsulent", row.GetProperty("position").GetString());
    }

    // ═════════════════════════════════════════════════════════════════════
    // The owner's marker (B0 / TASK-14116) — present AND cancelled, on all three surfaces
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>The roster must surface the scheduled change's DATE, not just hide the position.</summary>
    [Fact]
    public async Task Roster_ScheduledChangeMarker_ShowsTheDate_WhenAChangeIsScheduled()
    {
        var employeeId = await SeedEmployeeAsync("Roster Marker Present Test");
        var scheduledFrom = F.AddDays(25);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), scheduledFrom, "Today"),
            (scheduledFrom, null, "Future"));

        var client = AdminClient();
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{OrgId}/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var row = Assert.Single(body.GetProperty("employees").EnumerateArray()
            .Where(e => e.GetProperty("employeeId").GetString() == employeeId));

        // RED: fails if the roster has no scheduledChangeFrom member at all, or if it is null despite
        // a real scheduled change existing.
        Assert.True(row.TryGetProperty("scheduledChangeFrom", out var marker),
            "expected the roster row to carry scheduledChangeFrom (B0 / TASK-14116).");
        Assert.Equal(scheduledFrom.ToString("yyyy-MM-dd"), marker.GetString());
    }

    /// <summary>
    /// The case that matters most (per the sprint-end review): a scheduled change that was CANCELLED
    /// — retired to a zero-width row, the trace a soft-delete leaves behind (S141 B4) — must read as
    /// null, never as the cancelled date. Reporting a called-off change is worse than reporting
    /// nothing, because it is confidently wrong.
    /// </summary>
    [Fact]
    public async Task Roster_ScheduledChangeMarker_IsNull_WhenTheScheduledChangeWasCancelled()
    {
        var employeeId = await SeedEmployeeAsync("Roster Marker Cancelled Test");
        var cancelledFrom = F.AddDays(25);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), null, "Today"),
            (cancelledFrom, cancelledFrom, "NeverTookEffect")); // zero-width — retired, not scheduled

        var client = AdminClient();
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{OrgId}/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var row = Assert.Single(body.GetProperty("employees").EnumerateArray()
            .Where(e => e.GetProperty("employeeId").GetString() == employeeId));

        // RED: fails if the zero-width exclusion is missing and the roster reports the cancelled
        // date as though it were still scheduled.
        Assert.True(row.TryGetProperty("scheduledChangeFrom", out var marker));
        Assert.Equal(JsonValueKind.Null, marker.ValueKind);
    }

    /// <summary>The merged-admin overlay's people section must show the same marker.</summary>
    [Fact]
    public async Task PersonSearchOverlay_ScheduledChangeMarker_ShowsTheDate_WhenAChangeIsScheduled()
    {
        var employeeId = await SeedEmployeeAsync("Overlay Marker Present Test Zzq");
        var scheduledFrom = F.AddDays(25);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), scheduledFrom, "Today"),
            (scheduledFrom, null, "Future"));

        var client = AdminClient();
        var rsp = await client.GetAsync("/api/admin/search?q=Overlay+Marker+Present+Test+Zzq");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var row = Assert.Single(body.GetProperty("people").EnumerateArray()
            .Where(p => p.GetProperty("userId").GetString() == employeeId));

        Assert.True(row.TryGetProperty("scheduledChangeFrom", out var marker),
            "expected the overlay's people row to carry scheduledChangeFrom (B0 / TASK-14116).");
        Assert.Equal(scheduledFrom.ToString("yyyy-MM-dd"), marker.GetString());
    }

    /// <summary>The overlay half of the "must not announce a cancelled change" requirement.</summary>
    [Fact]
    public async Task PersonSearchOverlay_ScheduledChangeMarker_IsNull_WhenTheScheduledChangeWasCancelled()
    {
        var employeeId = await SeedEmployeeAsync("Overlay Marker Cancelled Test Zzq");
        var cancelledFrom = F.AddDays(25);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), null, "Today"),
            (cancelledFrom, cancelledFrom, "NeverTookEffect"));

        var client = AdminClient();
        var rsp = await client.GetAsync("/api/admin/search?q=Overlay+Marker+Cancelled+Test+Zzq");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var row = Assert.Single(body.GetProperty("people").EnumerateArray()
            .Where(p => p.GetProperty("userId").GetString() == employeeId));

        Assert.True(row.TryGetProperty("scheduledChangeFrom", out var marker));
        Assert.Equal(JsonValueKind.Null, marker.ValueKind);
    }

    /// <summary>
    /// The person-reference resolver — the roster's <c>nameResolution</c> cross-reference map, keyed
    /// by every structural-approver / cross-unit-leader id the roster names. A second ("reportee")
    /// employee whose PRIMARY reporting line names the subject as manager is the minimal fixture that
    /// gets the subject's id into the resolver's input set (<c>ApprovalPeriodRepository</c>'s
    /// <c>referencedIds</c>).
    /// </summary>
    [Fact]
    public async Task PersonReference_NameResolutionMarker_ShowsTheDate_WhenAChangeIsScheduled()
    {
        var managerId = await SeedEmployeeAsync("Manager Marker Present Test");
        var scheduledFrom = F.AddDays(25);
        await ReplaceProfileTimelineAsync(managerId,
            (F.AddDays(-400), scheduledFrom, "Today"),
            (scheduledFrom, null, "Future"));
        var reporteeId = await SeedEmployeeAsync("Reportee Of Present Manager Test");
        await SeedPrimaryReportingLineAsync(reporteeId, managerId);

        var client = AdminClient();
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{OrgId}/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // RED: fails if the manager never made it into nameResolution at all (the fixture/census is
        // wrong) or if the entry exists without the marker.
        Assert.True(body.GetProperty("nameResolution").TryGetProperty(managerId, out var refEntry),
            "expected the manager to be resolved in nameResolution via the reportee's PRIMARY reporting line.");
        Assert.True(refEntry.TryGetProperty("scheduledChangeFrom", out var marker));
        Assert.Equal(scheduledFrom.ToString("yyyy-MM-dd"), marker.GetString());
    }

    /// <summary>The person-reference half of the "must not announce a cancelled change" requirement —
    /// arguably the most consequential of the six, since a name chip is exactly where HR would act on
    /// a cross-reference without opening the referenced person's own profile.</summary>
    [Fact]
    public async Task PersonReference_NameResolutionMarker_IsNull_WhenTheScheduledChangeWasCancelled()
    {
        var managerId = await SeedEmployeeAsync("Manager Marker Cancelled Test");
        var cancelledFrom = F.AddDays(25);
        await ReplaceProfileTimelineAsync(managerId,
            (F.AddDays(-400), null, "Today"),
            (cancelledFrom, cancelledFrom, "NeverTookEffect"));
        var reporteeId = await SeedEmployeeAsync("Reportee Of Cancelled Manager Test");
        await SeedPrimaryReportingLineAsync(reporteeId, managerId);

        var client = AdminClient();
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{OrgId}/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("nameResolution").TryGetProperty(managerId, out var refEntry));
        Assert.True(refEntry.TryGetProperty("scheduledChangeFrom", out var marker));
        Assert.Equal(JsonValueKind.Null, marker.ValueKind);
    }

    // ─── Seeding helpers ─────────────────────────────────────────────────

    private async Task<string> SeedEmployeeAsync(string displayName)
    {
        var employeeId = "emp_s141_vis_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, employment_category,
                               is_active)
            VALUES (@u, @u, 'dev-only', @name, NULL, @org, 'AC', 'OK24', 'Standard', TRUE)
            """, conn))
        {
            cmd.Parameters.AddWithValue("u", employeeId);
            cmd.Parameters.AddWithValue("name", displayName);
            cmd.Parameters.AddWithValue("org", OrgId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var agreementCmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, 'AC', '0001-01-01', NULL, 1)
            """, conn))
        {
            agreementCmd.Parameters.AddWithValue("u", employeeId);
            await agreementCmd.ExecuteNonQueryAsync();
        }
        return employeeId;
    }

    private async Task ReplaceProfileTimelineAsync(
        string employeeId, params (DateOnly From, DateOnly? To, string Position)[] rows)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var del = new NpgsqlCommand(
            "DELETE FROM employee_profiles WHERE employee_id = @e", conn))
        {
            del.Parameters.AddWithValue("e", employeeId);
            await del.ExecuteNonQueryAsync();
        }
        var version = 1L;
        foreach (var row in rows)
        {
            await using var ins = new NpgsqlCommand(
                """
                INSERT INTO employee_profiles
                    (profile_id, employee_id, part_time_fraction, position, employment_category,
                     effective_from, effective_to, version)
                VALUES (gen_random_uuid(), @e, 1.000, @p, 'Standard', @from, @to, @v)
                """, conn);
            ins.Parameters.AddWithValue("e", employeeId);
            ins.Parameters.AddWithValue("p", row.Position);
            ins.Parameters.AddWithValue("from", row.From);
            ins.Parameters.AddWithValue("to", (object?)row.To ?? DBNull.Value);
            ins.Parameters.AddWithValue("v", version++);
            await ins.ExecuteNonQueryAsync();
        }
    }

    /// <summary>A PRIMARY reporting line naming <paramref name="managerId"/> as
    /// <paramref name="employeeId"/>'s structural approver — the minimal fixture that puts the
    /// manager's id into the roster's <c>nameResolution</c> input set
    /// (<c>ApprovalPeriodRepository.referencedIds</c>).</summary>
    private async Task SeedPrimaryReportingLineAsync(string employeeId, string managerId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines
                (employee_id, manager_id, organisation_id, relationship,
                 effective_from, effective_to, source, created_by)
            VALUES (@e, @m, @org, 'PRIMARY', @from, NULL, 'MANUAL', 'test-seed')
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("m", managerId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("from", F.AddDays(-100));
        await cmd.ExecuteNonQueryAsync();
    }

    // ─── HTTP helpers ────────────────────────────────────────────────────

    private HttpClient AdminClient()
    {
        var client = _fixedHost.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return client;
    }

    private static string MintGlobalAdminToken()
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return svc.GenerateToken(
            employeeId: "ADMIN_S141_VIS",
            name: "S141 Visibility Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }
}
