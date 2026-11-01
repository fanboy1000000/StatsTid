using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// S147 / TASK-14710-T (AC-230-1, AC-230-5 "others", AC-230-14 K-2 and K-3; owner ruling OQ-2) — the
/// leaver's grace, proven against the live automatic switch-off job (Step A).
///
/// <para><b>Plain language — Mette.</b> Mette's last working day is Friday 13 March 2026 (call it D). Her
/// manager can still approve, reject or reopen her March through day 5 (18 March) exactly as before, because
/// the system no longer switches her off the next morning: the switch-off waits until day 6 (19 March). From
/// day 1 Mette herself can neither log in nor change her own data (those are the other two Docker classes,
/// <c>LeaverLoginTests</c> and <c>DepartedSelfServiceTests</c>); HR can still correct her record on every day.
/// A leaver inside the grace is also not settled as an ordinary active employee (the særlige feriedage pass,
/// K-3) and not yet terminated (K-2) until the switch-off lands on day 6.</para>
///
/// <para><b>Harness.</b> One Postgres container (the real <c>init.sql</c>) and, per fact, one host whose clock is
/// pinned with <see cref="FixedTimeProvider"/> and which KEEPS the leaver job (<c>SettlementCloseService</c>):
/// every fact needs the real Step A to run its immediate first poll against the seeded state. People are seeded
/// BEFORE the host boots (so that poll sees them) and each fact carries a "witness" leaver that is due (D+6) —
/// when the witness has been switched off, Step A's whole candidate list has been processed, so a grace leaver
/// that is still active was genuinely skipped, not merely not yet reached.</para>
///
/// <para><b>Docker</b> — CI-pending, never locally green. Kinds are named per fact.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class LeaverGraceTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    /// <summary>Mette's last day employed: Friday 2026-03-13 (D).</summary>
    private static readonly DateOnly LastDay = new(2026, 3, 13);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private readonly List<WebApplicationFactory<Program>> _hosts = new();

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        // The leaver job is KEPT (default): every fact below needs the real Step A. Seeding happens
        // before each fact boots its derived host so the immediate first poll observes the people.
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        foreach (var h in _hosts) h.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // AC-230-1 — the manager acts on D+5.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-230-1. Kind F. On D+5 (Wednesday 2026-03-18) Step A runs and leaves Mette ACTIVE, and her manager's
    /// approve, reject and reopen of her EMPLOYEE_APPROVED March each answer 200 and move the period
    /// (APPROVED / REJECTED / DRAFT). A due witness leaver (D+6) is switched off by the same poll, proving the
    /// poll ran. Red conditions: <c>Assert.True(mette.IsActive)</c> — today Step A flips her on D+1, so the
    /// manager's call meets an inactive subject and answers 403 (the validator reads active users only).
    /// </summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("reopen")]
    public async Task OnDay5_StepAKeepsMetteActive_ManagerApproveRejectReopen_200(string action)
    {
        var today = LastDay.AddDays(5); // D+5
        var mette = await SeedEmployeeAsync("lg_mette_" + action);
        var manager = await SeedLeaderAsync("lg_mgr_" + action, manages: mette);
        await SetEndDateAsync(mette, LastDay);
        var witness = await SeedEmployeeAsync("lg_wit_" + action);
        await SetEndDateAsync(witness, today.AddDays(-6)); // due: D+6 for the witness
        var periodId = await SeedPeriodAsync(mette, "EMPLOYEE_APPROVED");

        var host = BootFixedHost(today);

        Assert.True(await WaitForInactiveAsync(witness),
            "Step A must flip the due witness (D+6), proving the poll processed its whole candidate list.");
        var tuple = await ReadUserAsync(mette);
        Assert.True(tuple.IsActive, "D+5 is the last day of the manager's grace: Step A must leave Mette active.");
        Assert.False(tuple.EndDateDeactivated);

        using var client = LeaderClient(host, manager);
        var rsp = action switch
        {
            "approve" => await client.PostAsync($"/api/approval/{periodId}/approve", content: null),
            "reject" => await client.PostAsJsonAsync($"/api/approval/{periodId}/reject", new { reason = "s147 grace pin" }),
            _ => await client.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "s147 grace pin" }),
        };
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"the manager's {action} on D+5 must be 200 (the grace), got {(int)rsp.StatusCode}: {raw}");
        Assert.Equal(action switch { "approve" => "APPROVED", "reject" => "REJECTED", _ => "DRAFT" },
            await ReadPeriodStatusAsync(periodId));
    }

    /// <summary>
    /// AC-230-1 / AC-230-3, the poll. Kind F. One host on D+5 with five leavers whose last day was 1, 2, 3, 4 and
    /// 5 days back (D+1 .. D+5) and a due witness (D+6): Step A switches the witness off and leaves ALL five grace
    /// leavers active, with no provenance claim, no version bump and no flip event. Red conditions:
    /// <c>Assert.True(IsActive)</c> for each grace leaver — today every one of them is flipped on its D+1.
    /// </summary>
    [Fact]
    public async Task StepA_Day1ThroughDay5_LeavesTheLeaverActive_Day6Flips()
    {
        var today = LastDay.AddDays(5);
        var grace = new List<(string Id, int DaysBack)>();
        for (var back = 1; back <= 5; back++)
        {
            var id = await SeedEmployeeAsync("lg_poll_d" + back);
            await SetEndDateAsync(id, today.AddDays(-back));
            grace.Add((id, back));
        }
        var witness = await SeedEmployeeAsync("lg_poll_d6");
        await SetEndDateAsync(witness, today.AddDays(-6));

        BootFixedHost(today);

        Assert.True(await WaitForInactiveAsync(witness), "the D+6 leaver must be switched off by the poll.");
        var flipped = await ReadUserAsync(witness);
        Assert.True(flipped.EndDateDeactivated);
        foreach (var (id, back) in grace)
        {
            var t = await ReadUserAsync(id);
            Assert.True(t.IsActive, $"D+{back} is inside the grace: Step A must not switch the leaver off.");
            Assert.False(t.EndDateDeactivated);
            Assert.Equal(1L, t.Version);
            Assert.Equal(0L, await CountFlipEventsAsync(id));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // AC-230-5, "others" — on D+1 HR and the manager still act for Mette.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-230-5 (others). Kind B on the HR leg (boundary pin, holds today: HR registering for someone else was
    /// always allowed) and kind F on the manager leg. On D+1 (Saturday 2026-03-14, still the grace) HR's time
    /// entry for Mette's last working week answers 201, and the manager's approve of her EMPLOYEE_APPROVED March
    /// answers 200. The new self-service rule must not over-reach to someone ELSE acting for her. The Step-A
    /// poll runs first (a due witness proves it ran). Red condition (manager leg): the 200 on the manager's approve —
    /// today Step A flips Mette on D+1, so the approve meets an inactive subject and answers 403.
    /// </summary>
    [Fact]
    public async Task OnDay1_HrWriteForMette_Allowed_ManagerApprove_200()
    {
        var today = LastDay.AddDays(1); // D+1
        var mette = await SeedEmployeeAsync("lg_oth_mette");
        var manager = await SeedLeaderAsync("lg_oth_mgr", manages: mette);
        await SetEndDateAsync(mette, LastDay);
        var witness = await SeedEmployeeAsync("lg_oth_wit");
        await SetEndDateAsync(witness, today.AddDays(-6));

        var host = BootFixedHost(today);
        Assert.True(await WaitForInactiveAsync(witness), "the poll must have run.");

        using (var hr = ClientFor(host, "lg_oth_hr", StatsTidRoles.LocalHR))
        {
            var entry = await hr.PostAsJsonAsync("/api/time-entries", new
            {
                employeeId = mette, date = new DateOnly(2026, 3, 10), hours = 7.4m,
                taskId = (string?)null, activityType = "NORMAL", agreementCode = "AC",
            });
            var entryRaw = await entry.Content.ReadAsStringAsync();
            Assert.True(entry.StatusCode == HttpStatusCode.Created,
                $"HR's write for Mette on D+1 must be allowed, got {(int)entry.StatusCode}: {entryRaw}");
        }

        // The month is sealed only AFTER HR's registration (an EMPLOYEE_APPROVED month would refuse a time entry).
        var periodId = await SeedPeriodAsync(mette, "EMPLOYEE_APPROVED");

        using var leader = LeaderClient(host, manager);
        var approve = await leader.PostAsync($"/api/approval/{periodId}/approve", content: null);
        var approveRaw = await approve.Content.ReadAsStringAsync();
        Assert.True(approve.StatusCode == HttpStatusCode.OK,
            $"the manager's approve on D+1 must be 200, got {(int)approve.StatusCode}: {approveRaw}");
        Assert.Equal("APPROVED", await ReadPeriodStatusAsync(periodId));
        Assert.True((await ReadUserAsync(mette)).IsActive);
    }

    // ════════════════════════════════════════════════════════════════════════
    // AC-230-14 — K-3 (særlige feriedage active pass) and K-2 (termination from D+6).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-230-14, K-3. Kind B — boundary pin, holds today: a grace leaver gets no SPECIAL_HOLIDAY settlement row.
    /// It holds today through the in-lock leaver guard (<c>VacationSettlementService</c> (3b): a SPECIAL_HOLIDAY tuple
    /// whose subject has a passed end date is refused <c>NotDueUnderLock</c>), so this fact does NOT by itself prove the
    /// K-3 SQL filter: with only that filter reverted the pass picks the leaver up, the guard refuses, and the fact stays
    /// green. What the K-3 filter adds is tidiness (the leaver is never picked up, no lock taken, no NotDue churn); the
    /// selection step cannot be observed from a test without touching src, so that part is not pinned here. The fact
    /// guards the OUTCOME against any future change that removes both layers.
    ///
    /// <para>Seeding: the settlement gates are ENABLED (go-live 2020-01-01 and the SPECIAL_HOLIDAY flag). Two ordinary
    /// active employees bracket the leaver in <c>user_id</c> order (the pass enumerates by <c>ORDER BY user_id</c>): an
    /// "a" control that sorts before and a "z" control that sorts after. Waiting for the LATER control's row means the
    /// pass has reached past the leaver's position, so the zero count is read after the leaver was passed over, not before
    /// the pass got to it. The leaver is a grace leaver (end date 3 days back, still <c>is_active</c>). It also has no
    /// VACATION settlement (the VACATION active branch excludes a passed end date: also a boundary pin).</para>
    /// </summary>
    [Fact]
    public async Task K3_GraceLeaver_GetsNoSpecialHolidaySettlementRow()
    {
        var today = new DateOnly(2026, 6, 1);
        var control = await SeedEmployeeAsync("lg_k3_a_control");   // sorts before the leaver
        var leaver = await SeedEmployeeAsync("lg_k3_m_leaver");
        var lateControl = await SeedEmployeeAsync("lg_k3_z_control"); // sorts after the leaver
        await SetEndDateAsync(leaver, today.AddDays(-3)); // D+3, active

        BootFixedHost(today, goLive: new DateOnly(2020, 1, 1), specialHolidayEnabled: true);

        Assert.True(await WaitForSettlementAsync(control, "SPECIAL_HOLIDAY"),
            "the control employee must be settled by the særlige feriedage pass (the gates are open, the pass ran).");
        Assert.True(await WaitForSettlementAsync(lateControl, "SPECIAL_HOLIDAY"),
            "the later control must be settled too: the pass has then gone past the leaver in user_id order.");
        Assert.Equal(0L, await CountSettlementsAsync(leaver, "SPECIAL_HOLIDAY"));
        Assert.Equal(0L, await CountSettlementsAsync(leaver));
        Assert.True((await ReadUserAsync(leaver)).IsActive);
    }

    /// <summary>
    /// AC-230-14, K-2. Kind F. Termination settlement follows the switch-off: with the go-live gate open, a leaver
    /// whose last day was 6 days back is switched off by Step A and gets a TERMINATION settlement in the same poll,
    /// while a leaver 5 days back — still active, in the grace — gets no settlement row. Red conditions:
    /// <c>Assert.Equal(0L, graceRows)</c> and <c>Assert.True(graceLeaver.IsActive)</c> — today the D+5 leaver was
    /// switched off (and terminated) days earlier. <c>// S147 OQ-2:</c> re-pinned from D+1 to D+6.
    /// </summary>
    [Fact]
    public async Task K2_TerminationSettlement_IsDueFromDay6_NotDay1()
    {
        var today = new DateOnly(2026, 6, 1);
        var due = await SeedEmployeeAsync("lg_k2_due");
        await SetEndDateAsync(due, today.AddDays(-6)); // D+6: S147 OQ-2 (was D+1)
        var grace = await SeedEmployeeAsync("lg_k2_grace");
        await SetEndDateAsync(grace, today.AddDays(-5)); // D+5: still the grace

        BootFixedHost(today, goLive: new DateOnly(2020, 1, 1));

        Assert.True(await WaitForSettlementAsync(due, "VACATION"),
            "the D+6 leaver must be terminated (settled) once Step A has switched her off.");
        Assert.False((await ReadUserAsync(due)).IsActive);
        Assert.Equal(0L, await CountSettlementsAsync(grace));
        Assert.True((await ReadUserAsync(grace)).IsActive);
    }

    // ─────────────────────────────── hosts and clients ───────────────────────────────

    private WebApplicationFactory<Program> BootFixedHost(
        DateOnly today, DateOnly? goLive = null, bool? specialHolidayEnabled = null)
    {
        var host = _factory.WithWebHostBuilder(builder =>
        {
            var cfg = new Dictionary<string, string?>();
            if (goLive is not null)
                cfg["Settlement:GoLiveDate"] = goLive.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (specialHolidayEnabled is not null)
                cfg["Settlement:SpecialHolidaySettlementEnabled"] = specialHolidayEnabled.Value ? "true" : "false";
            if (cfg.Count > 0)
                builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(cfg));
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(today)));
        });
        _ = host.CreateClient(); // host build + hosted-service start (immediate poll)
        _hosts.Add(host);
        return host;
    }

    private static HttpClient ClientFor(WebApplicationFactory<Program> host, string actorId, string role)
    {
        var client = host.CreateClient();
        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid", Audience = "statstid", SigningKey = DevFallbackSigningKey, ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: actorId, name: actorId, role: role, agreementCode: "AC", orgId: OrgId,
            scopes: new[] { new RoleScope(role, OrgId, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient LeaderClient(WebApplicationFactory<Program> host, string managerId)
        => ClientFor(host, managerId, StatsTidRoles.LocalLeader);

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync(string prefix)
    {
        var id = $"{prefix}_{Guid.NewGuid().ToString("N")[..6]}";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, id, OrgId, "AC", "OK24");
        return id;
    }

    /// <summary>A LocalLeader who is the employee's designated PRIMARY manager (reporting line) — the edge the
    /// approve / reject / reopen routes check when the leader's role scope alone does not clear the HR floor.</summary>
    private async Task<string> SeedLeaderAsync(string prefix, string manages)
    {
        var leader = await SeedEmployeeAsync(prefix);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var ra = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@u, 'LOCAL_LEADER', @org, 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            ra.Parameters.AddWithValue("u", leader);
            ra.Parameters.AddWithValue("org", OrgId);
            await ra.ExecuteNonQueryAsync();
        }
        await using (var rl = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines
                (employee_id, manager_id, organisation_id, relationship, effective_from, created_by)
            VALUES (@e, @m, @org, 'PRIMARY', @from, 'test_s147_lg')
            """, conn))
        {
            rl.Parameters.AddWithValue("e", manages);
            rl.Parameters.AddWithValue("m", leader);
            rl.Parameters.AddWithValue("org", OrgId);
            rl.Parameters.AddWithValue("from", new DateOnly(2024, 1, 1));
            await rl.ExecuteNonQueryAsync();
        }
        return leader;
    }

    private async Task SetEndDateAsync(string employeeId, DateOnly endDate)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_end_date = @d, updated_at = NOW() WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        cmd.Parameters.AddWithValue("d", endDate);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedPeriodAsync(string employeeId, string status)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (employee_id, org_id, period_start, period_end,
                                          period_type, status, agreement_code, ok_version)
            VALUES (@e, @org, @s, @en, 'MONTHLY', @st, 'AC', 'OK24')
            RETURNING period_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("s", new DateOnly(2026, 3, 1));
        cmd.Parameters.AddWithValue("en", new DateOnly(2026, 3, 31));
        cmd.Parameters.AddWithValue("st", status);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    // ─────────────────────────────── reads and waits ───────────────────────────────

    private async Task<(bool IsActive, bool EndDateDeactivated, long Version)> ReadUserAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT is_active, end_date_deactivated, version FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), $"user {employeeId} must exist");
        return (r.GetBoolean(0), r.GetBoolean(1), r.GetInt64(2));
    }

    private async Task<string> ReadPeriodStatusAsync(Guid periodId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Settlement rows for the employee (any state), optionally of one entitlement type.</summary>
    private async Task<long> CountSettlementsAsync(string employeeId, string? entitlementType = null)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM vacation_settlements WHERE employee_id = @e AND (@t::text IS NULL OR entitlement_type = @t)", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", (object?)entitlementType ?? DBNull.Value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> CountFlipEventsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'EmployeeEndDateDeactivationApplied'", conn);
        cmd.Parameters.AddWithValue("s", $"employee-{employeeId}");
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<bool> WaitForInactiveAsync(string employeeId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (!(await ReadUserAsync(employeeId)).IsActive) return true;
            await Task.Delay(250);
        }
        return false;
    }

    private async Task<bool> WaitForSettlementAsync(string employeeId, string entitlementType)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await CountSettlementsAsync(employeeId, entitlementType) >= 1) return true;
            await Task.Delay(250);
        }
        return false;
    }
}
