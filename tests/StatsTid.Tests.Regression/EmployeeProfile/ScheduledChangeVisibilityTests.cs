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
/// S141 / TASK-14106, wave 2 — pin 6: the AS-OF-TODAY reads that put an employee's position on a
/// screen OTHER than the profile page/edit drawer, with a scheduled future change present.
///
/// <para>
/// <b>Why this matters, in plain language.</b> Before this sprint, an employee's position never
/// changed except by a today-dated edit, so any read of "the row with no end date" was always
/// today's truth. Once HR can schedule a promotion for a future date, a read that still follows the
/// OPEN row would show the roster (and the merged-admin overlay's people search) a job title that is
/// not yet in force — weeks early, with nothing on screen saying so. TASK-14102 (wave 1, already
/// merged) converted the underlying reads to as-of-today; this file is the HTTP-level proof that the
/// conversion actually reaches the two screens that display it.
/// </para>
///
/// <para>
/// <b>Scope note, found while writing this file rather than assumed from the refinement's prose.</b>
/// The refinement names "the organisation roster, people search and the person-reference resolver"
/// as the three affected surfaces. Reading the actual endpoints (<c>AdminEndpoints.cs</c>) shows
/// this maps to exactly TWO HTTP surfaces, not three: <c>GET
/// /api/admin/reporting-lines/tree/{organisationId}/medarbejdere</c> (the roster) carries position
/// on both its employee rows AND its <c>nameResolution</c> dictionary (the "person-reference
/// resolver" the refinement names is this same endpoint's cross-reference lookup, not a separate
/// route) — and <c>GET /api/admin/search</c> (the merged-admin overlay's people section, which is
/// what "people search" means here). <c>GET /api/admin/users/search</c> — a DIFFERENT, older
/// person-search endpoint — was deliberately left untouched by TASK-14102 (per its own doc comment:
/// "the existing roster/picker reads are untouched") and never surfaced position on the wire at all,
/// so it is out of scope for this pin and not tested here. This file only covers the two employee
/// row / people-section assertions; it does NOT attempt to construct a scenario where this employee
/// appears in the roster's <c>nameResolution</c> cross-reference (that requires being referenced as
/// a structural approver or cross-unit leader elsewhere, which is a materially larger fixture setup
/// for the same underlying SQL predicate already pinned via the employee row) — flagged in the
/// final report as a narrower cut than the refinement's full census, made deliberately for cost.
/// </para>
///
/// <para>
/// <b>RED-FIRST.</b> Written from the spec; Docker is unavailable locally, so nothing here is
/// verified and nothing is reported as passing before the wave-2 gate. Unlike this file's sibling
/// endpoint-dependent pins, this ONE'S underlying reads (B1, wave 1) are already merged — so it is
/// plausible this pin is closer to green than the others, but that is still unverified locally and
/// must not be reported as passing.
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
