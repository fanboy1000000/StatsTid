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

namespace StatsTid.Tests.Regression.UserAgreementCode;

/// <summary>
/// S141 / TASK-14106, wave 2 — the ENDPOINT-dependent AGREEMENT-CODE-side pins for Increment 4's
/// "schedule a change ahead" feature, across BOTH surfaces that can write dated agreement history:
/// the general <c>PUT /api/admin/users/{id}</c> (agreement code bundled in) and the dedicated
/// <c>PUT /api/admin/users/{id}/agreement-code</c>.
///
/// <para>
/// <b>Why the agreement code needs its own file, in plain language.</b> The refinement's own
/// Step-0b review caught that B0's visibility requirement and OQ-6's edit prompt were first scoped
/// to the profile fields alone — but the agreement code is a SECOND dated field shown in the same
/// edit drawer, written on every save, and a scheduled agreement change would otherwise be exactly
/// the owner's original defect one field over: HR sees a value on screen with no indication a
/// different one is already scheduled, or edits today without being told the edit will silently
/// expire the day the scheduled change begins.
/// </para>
///
/// <para>
/// <b>Written RED, against wave-2 code that has since merged.</b> TASK-14104 has landed; Docker is
/// still unavailable on this machine, so nothing here is verified locally and nothing is reported as
/// passing regardless.
/// </para>
///
/// <para>
/// <b>Wire-contract note, corrected at the sprint-end review (W1).</b> Two names were originally
/// written as provisional guesses. The request field, <c>carryForwardToScheduledChange</c>, matched
/// what TASK-14104 shipped. The read-payload member did NOT: this file asserted a bare
/// <c>scheduled</c> member (mirroring the profile side's actual name), but the merged contract names
/// it <see cref="StatsTid.Backend.Api.Contracts.UserDetailResponse.ScheduledAgreementCode"/> —
/// <c>scheduledAgreementCode</c> on the wire — because the users GET is a shared resource carrying
/// several dated facts, and a bare "scheduled" would have been ambiguous the moment a second one was
/// added. Both facts below now assert the real name; the earlier guess would have produced a FALSE
/// RED against a correct payload, which is worse than no pin (a false red that resembles the pre-fix
/// defect costs a debugging session and risks a correct fix being reverted).
/// </para>
///
/// <para>Conventions mirror <see cref="AgreementCodeBackdatingEndpointTests"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ScheduledAgreementChangeEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _fixedHost = null!;

    /// <summary>The ONE pinned "today" for this suite (PAT-008) — matches the sibling S141 suites.
    /// 2025-03-12, a WEDNESDAY, safely on the OK24 side of the OK24→OK26 cutover.</summary>
    private static readonly DateOnly F = new(2025, 3, 12);

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _fixedHost = _factory.WithFixedToday(F);
        _ = _fixedHost.CreateClient(); // PAT-008 boot order
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

    // ═════════════════════════════════════════════════════════════════════
    // 4. OQ-6 — the two branches, on BOTH agreement-code-writing endpoints
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Pin 4c (OQ-6, branch 1, the general users PUT). The default / "apply until the
    /// scheduled change" behaviour — the pre-existing split, pinned explicitly.</summary>
    [Fact]
    public async Task PUT_User_TodayDatedAgreementEdit_ApplyUntilScheduledChange_LeavesScheduledRowUntouched()
    {
        var userId = await SeedUserAsync("AC");
        var scheduledFrom = F.AddDays(30);
        await ReplaceAgreementTimelineAsync(userId,
            (F.AddDays(-400), scheduledFrom, "AC"),
            (scheduledFrom, null, "HK"));

        var client = AdminClient();
        var version = await ReadUsersVersionAsync(client, userId);
        var rsp = await PutUserAsync(client, userId, new
        {
            agreementCode = "PROSA",
            effectiveFrom = F.ToString("yyyy-MM-dd"),
            carryForwardToScheduledChange = false,
        }, $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var rows = await ReadAgreementTimelineAsync(userId);
        var todayRow = rows.Single(r => r.From == F);
        Assert.Equal(scheduledFrom, todayRow.To);
        Assert.Equal("PROSA", todayRow.Code);

        // RED: fails if the edit silently reached into the scheduled row instead of expiring at the
        // scheduled date.
        var scheduledRow = rows.Single(r => r.From == scheduledFrom);
        Assert.Equal("HK", scheduledRow.Code);
    }

    /// <summary>Pin 4d (OQ-6, branch 2, the general users PUT). "Carry forward" must move the
    /// scheduled row's code to the newly-edited value.</summary>
    [Fact]
    public async Task PUT_User_TodayDatedAgreementEdit_CarryForward_UpdatesScheduledRowsCode()
    {
        var userId = await SeedUserAsync("AC");
        var scheduledFrom = F.AddDays(30);
        await ReplaceAgreementTimelineAsync(userId,
            (F.AddDays(-400), scheduledFrom, "AC"),
            (scheduledFrom, null, "HK"));

        var client = AdminClient();
        var version = await ReadUsersVersionAsync(client, userId);
        var rsp = await PutUserAsync(client, userId, new
        {
            agreementCode = "PROSA",
            effectiveFrom = F.ToString("yyyy-MM-dd"),
            carryForwardToScheduledChange = true,
        }, $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var rows = await ReadAgreementTimelineAsync(userId);
        var scheduledRow = rows.Single(r => r.From == scheduledFrom);
        // RED: fails if carry-forward is not implemented, or implemented only on the profile side —
        // the scheduled row would still read "HK" instead of the newly-edited "PROSA".
        Assert.Equal("PROSA", scheduledRow.Code);
    }

    /// <summary>Pin 4e (OQ-6, both branches, the DEDICATED agreement-code endpoint). This surface is
    /// the leaver path (terminated-inclusive) and must offer the same choice, per the refinement's
    /// explicit "on all three endpoints" scope. Both branches in one test since the endpoint carries
    /// only the one field.</summary>
    [Fact]
    public async Task PUT_AgreementCode_TodayDatedEdit_BothBranches()
    {
        // Apply-until.
        var userA = await SeedUserAsync("AC");
        var scheduledFromA = F.AddDays(30);
        await ReplaceAgreementTimelineAsync(userA,
            (F.AddDays(-400), scheduledFromA, "AC"),
            (scheduledFromA, null, "HK"));
        var clientA = AdminClient();
        var versionA = await ReadUsersVersionAsync(clientA, userA);
        var rspA = await PutAgreementCodeAsync(clientA, userA, "PROSA", F, versionA, carryForward: false);
        Assert.Equal(HttpStatusCode.OK, rspA.StatusCode);
        var rowsA = await ReadAgreementTimelineAsync(userA);
        Assert.Equal("HK", rowsA.Single(r => r.From == scheduledFromA).Code);

        // Carry-forward.
        var userB = await SeedUserAsync("AC");
        var scheduledFromB = F.AddDays(30);
        await ReplaceAgreementTimelineAsync(userB,
            (F.AddDays(-400), scheduledFromB, "AC"),
            (scheduledFromB, null, "HK"));
        var clientB = AdminClient();
        var versionB = await ReadUsersVersionAsync(clientB, userB);
        var rspB = await PutAgreementCodeAsync(clientB, userB, "PROSA", F, versionB, carryForward: true);
        Assert.Equal(HttpStatusCode.OK, rspB.StatusCode);
        var rowsB = await ReadAgreementTimelineAsync(userB);
        // RED: fails if the dedicated agreement-code endpoint never got OQ-6's request field at all
        // (only two of the three endpoints wired) — the scheduled row would still read "HK".
        Assert.Equal("PROSA", rowsB.Single(r => r.From == scheduledFromB).Code);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 5. B0 — the read payload carries the scheduled change (agreement side)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Pin 5 (B0, agreement side). The user GET must carry the next scheduled
    /// agreement-code change in the same payload, mirroring the profile-side requirement.</summary>
    [Fact]
    public async Task GET_User_WithScheduledAgreementChangePresent_PayloadCarriesIt()
    {
        var userId = await SeedUserAsync("AC");
        var scheduledFrom = F.AddDays(30);
        await ReplaceAgreementTimelineAsync(userId,
            (F.AddDays(-400), scheduledFrom, "AC"),
            (scheduledFrom, null, "HK"));

        var client = AdminClient();
        var rsp = await client.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.TryGetProperty("scheduledAgreementCode", out var scheduled),
            "expected the users GET payload to carry the next scheduled agreement-code change (B0).");
        Assert.NotEqual(JsonValueKind.Null, scheduled.ValueKind);
        Assert.Equal(scheduledFrom.ToString("yyyy-MM-dd"), scheduled.GetProperty("effectiveFrom").GetString());
        Assert.Equal("HK", scheduled.GetProperty("agreementCode").GetString());
    }

    /// <summary>The negative case, mirroring the profile side: explicit null, not merely absent.</summary>
    [Fact]
    public async Task GET_User_WithNoScheduledAgreementChange_PayloadCarriesExplicitNull()
    {
        var userId = await SeedUserAsync("AC");
        await ReplaceAgreementTimelineAsync(userId, (F.AddDays(-400), null, "AC"));

        var client = AdminClient();
        var rsp = await client.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.TryGetProperty("scheduledAgreementCode", out var scheduled));
        Assert.Equal(JsonValueKind.Null, scheduled.ValueKind);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 7. A future write leaves the agreement_code cache untouched
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Pin 7 (agreement half). <c>users.agreement_code</c> feeds the login token and ~200
    /// live-only reads and means "the code as of TODAY". A future-dated write must leave it alone —
    /// needs the dedicated endpoint's future-date refusal LIFTED (TASK-14104, wave 2).</summary>
    [Fact]
    public async Task PUT_AgreementCode_FutureDated_LeavesLiveCacheUntouched()
    {
        var userId = await SeedUserAsync("AC");
        await ReplaceAgreementTimelineAsync(userId, (F.AddDays(-400), null, "AC"));

        var client = AdminClient();
        var version = await ReadUsersVersionAsync(client, userId);
        var scheduledFrom = F.AddDays(30);
        var rsp = await PutAgreementCodeAsync(client, userId, "HK", scheduledFrom, version, carryForward: null);

        // RED (wave-2 dependent): fails with 422 until TASK-14104 lifts the endpoint-side guard.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Equal("HK", await ReadDatedAgreementCodeAtAsync(userId, scheduledFrom));
        // The live cache — what the login mint and ~200 live-only reads use — must not have moved.
        Assert.Equal("AC", await ReadUsersAgreementCodeAsync(userId));
        Assert.Equal("AC", await ReadDatedAgreementCodeAtAsync(userId, F));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 10. B9 — the repository-internal version check, documented against a split write
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pin 10 (B9). The general users PUT locks the OPEN agreement-code row and passes ITS version
    /// as the repository's <c>expectedVersion</c> — a defence-in-depth check distinct from the
    /// CLIENT-facing token (<c>users.version</c>, validated separately). Before future-dating, "the
    /// open row" and "the row about to be split" were always the same row, so this check happened to
    /// guard exactly what was being written. Once a scheduled row exists, the open row is the FUTURE
    /// one, while a TODAY-dated write actually splits the HISTORY row that covers today — so the
    /// internal check is now validating a row the write never touches.
    /// </summary>
    /// <remarks>
    /// This is a DOCUMENTATION pin, not a behaviour change (per the refinement: "nothing breaks" —
    /// the check still trivially agrees with itself, since both the endpoint's pre-read and the
    /// repository's own open-row read select the SAME open row under the SAME lock). What this test
    /// proves is the fact the comment describes: the write's real target (the covering-today history
    /// row) changes correctly while the row the internal check actually validated (the future open
    /// row) is verifiably untouched — so a future regression that confused "the row the check
    /// approved" with "the row that was written" would be caught here.
    /// </remarks>
    [Fact]
    public async Task PUT_User_TodayDatedAgreementEdit_WithFutureRowPresent_SplitsHistoryRow_NotTheOpenFutureRow()
    {
        var userId = await SeedUserAsync("AC");
        var scheduledFrom = F.AddDays(30);
        await ReplaceAgreementTimelineAsync(userId,
            (F.AddDays(-400), scheduledFrom, "AC"),
            (scheduledFrom, null, "HK"));
        var futureRowVersionBefore = (await ReadAgreementTimelineAsync(userId))
            .Single(r => r.From == scheduledFrom).Version;

        var client = AdminClient();
        var version = await ReadUsersVersionAsync(client, userId);
        var rsp = await PutUserAsync(client, userId, new
        {
            agreementCode = "PROSA",
            effectiveFrom = F.ToString("yyyy-MM-dd"),
        }, $"\"{version}\"");

        // RED: fails if the internal open-row check (which now validates the FUTURE row, not the
        // history row this write actually touches) spuriously rejects a legitimate today-dated edit.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var rows = await ReadAgreementTimelineAsync(userId);
        Assert.Equal(3, rows.Count);
        // The write's real target — the history row covering today — was split correctly.
        var todayRow = rows.Single(r => r.From == F);
        Assert.Equal(scheduledFrom, todayRow.To);
        Assert.Equal("PROSA", todayRow.Code);
        // RED: fails if a future regression let the internal check's row (the open/future one) get
        // mutated instead of — or in addition to — the real anchor: its code and version must be
        // exactly as they were.
        var futureRowAfter = rows.Single(r => r.From == scheduledFrom);
        Assert.Equal("HK", futureRowAfter.Code);
        Assert.Equal(futureRowVersionBefore, futureRowAfter.Version);
    }

    // ─── Seeding helpers ─────────────────────────────────────────────────

    private async Task<string> SeedUserAsync(string agreementCode)
    {
        var userId = "usr_s141_ac_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, employment_category,
                               is_active)
            VALUES (@u, @u, 'dev-only', 'S141 Scheduled Agreement Test User', NULL,
                    @org, @code, 'OK24', 'Standard', TRUE)
            """, conn))
        {
            cmd.Parameters.AddWithValue("u", userId);
            cmd.Parameters.AddWithValue("org", OrgId);
            cmd.Parameters.AddWithValue("code", agreementCode);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var profileCmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles
                (profile_id, employee_id, part_time_fraction, employment_category, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, 1.000, 'Standard', '0001-01-01', NULL, 1)
            """, conn))
        {
            profileCmd.Parameters.AddWithValue("u", userId);
            await profileCmd.ExecuteNonQueryAsync();
        }
        return userId;
    }

    private async Task ReplaceAgreementTimelineAsync(
        string userId, params (DateOnly From, DateOnly? To, string Code)[] rows)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var del = new NpgsqlCommand(
            "DELETE FROM user_agreement_codes WHERE user_id = @u", conn))
        {
            del.Parameters.AddWithValue("u", userId);
            await del.ExecuteNonQueryAsync();
        }
        var version = 1L;
        foreach (var row in rows)
        {
            await using var ins = new NpgsqlCommand(
                """
                INSERT INTO user_agreement_codes
                    (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
                VALUES (gen_random_uuid(), @u, @c, @from, @to, @v)
                """, conn);
            ins.Parameters.AddWithValue("u", userId);
            ins.Parameters.AddWithValue("c", row.Code);
            ins.Parameters.AddWithValue("from", row.From);
            ins.Parameters.AddWithValue("to", (object?)row.To ?? DBNull.Value);
            ins.Parameters.AddWithValue("v", version++);
            await ins.ExecuteNonQueryAsync();
        }
    }

    // ─── Read helpers ────────────────────────────────────────────────────

    private sealed record AgreementRow(DateOnly From, DateOnly? To, string Code, long Version);

    private async Task<IReadOnlyList<AgreementRow>> ReadAgreementTimelineAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT effective_from, effective_to, agreement_code, version
            FROM user_agreement_codes WHERE user_id = @u ORDER BY effective_from
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        var rows = new List<AgreementRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AgreementRow(
                reader.GetFieldValue<DateOnly>(0),
                reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
                reader.GetString(2),
                reader.GetInt64(3)));
        }
        return rows;
    }

    private async Task<string?> ReadDatedAgreementCodeAtAsync(string userId, DateOnly asOf)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT agreement_code FROM user_agreement_codes
            WHERE user_id = @u AND effective_from <= @d
              AND (effective_to IS NULL OR @d < effective_to)
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("d", asOf);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task<string?> ReadUsersAgreementCodeAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT agreement_code FROM users WHERE user_id = @u", conn);
        cmd.Parameters.AddWithValue("u", userId);
        return await cmd.ExecuteScalarAsync() as string;
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
            employeeId: "ADMIN_S141_AC",
            name: "S141 Scheduled Agreement Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private static async Task<long> ReadUsersVersionAsync(HttpClient client, string userId)
    {
        var rsp = await client.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("version").GetInt64();
    }

    private static async Task<HttpResponseMessage> PutUserAsync(
        HttpClient client, string userId, object body, string ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    /// <summary><paramref name="carryForward"/> is the same PROVISIONAL <c>carryForwardToScheduledChange</c>
    /// field used on the profile-side file — see that class's doc for why the name is not fixed
    /// yet.</summary>
    private static async Task<HttpResponseMessage> PutAgreementCodeAsync(
        HttpClient client, string userId, string agreementCode, DateOnly effectiveFrom,
        long ifMatchVersion, bool? carryForward)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}/agreement-code")
        {
            Content = JsonContent.Create(new
            {
                agreementCode,
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
                carryForwardToScheduledChange = carryForward,
            }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchVersion}\"");
        return await client.SendAsync(req);
    }
}
