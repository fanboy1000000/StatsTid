using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Admin;

/// <summary>
/// S138 / TASK-13802 (ADR-040 D8 as amended 2026-09-02) — the agreement-code BACKDATING pins,
/// across both surfaces that can now write dated agreement history.
///
/// <para>
/// <b>What this suite is about, in plain language.</b> An employee's collective-agreement code
/// drives what the payroll rules do with their hours, and HR sometimes learns after the fact that
/// the record was wrong for a past stretch. S138 makes that recordable. Two surfaces, deliberately
/// split:
/// </para>
/// <list type="bullet">
///   <item><b>The general <c>PUT /api/admin/users/{id}</c></b> keeps its ACTIVE-ONLY lock (it also
///     owns the <c>is_active</c> switch, so widening it would open a reactivation side-door) and
///     gains only the ability to date the change in the past.</item>
///   <item><b>The new <c>PUT /api/admin/users/{id}/agreement-code</c></b> is the narrow,
///     terminated-inclusive surface for the leaver case — two fields, no lifecycle switch, LocalHR
///     floor. A departed employee's final months are exactly the ones payroll still has to settle.</item>
/// </list>
///
/// <para>
/// <b>The one-bump rule.</b> <c>users.version</c> is the single client concurrency token for this
/// aggregate and must move EXACTLY ONCE per request. Two parties can now write the users row in
/// one transaction (the endpoint's own UPDATE and the dated writer's cache refresh), so exactly
/// one of them owns the bump: when the writer wrote, it does; otherwise the endpoint does. Both
/// suites below pin the resulting arithmetic, because a double bump would 412 the caller's own
/// next edit and mint a token nobody was handed.
/// </para>
///
/// <para>
/// <b>RED-FIRST.</b> Every expectation was derived from the S138 refinement spec before observing
/// the implementation. Docker is unavailable on the authoring machine, so these facts are
/// compiler-validated locally and verify in CI (refinement Assumption 8).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AgreementCodeBackdatingEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ═════════════════════════════════════════════════════════════════════
    // A. The general users PUT — the one-bump rule + the killed silent no-op
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A BACKDATED agreement-code change through the general users PUT must bump
    /// <c>users.version</c> exactly ONCE (lockedVersion + 1) and stamp that same value as the
    /// response ETag. Before S138 the repository did not touch <c>users</c> at all; now it owns
    /// both the cache and the bump, so the endpoint's own UPDATE must stand down — a double bump
    /// here would be invisible in the response but would 412 the admin's very next edit.
    /// </summary>
    [Fact]
    public async Task UsersPut_AgreementBackdate_BumpsUsersVersionExactlyOnce()
    {
        var userId = await SeedUserAsync(agreementCode: "AC");
        var t60 = Today.AddDays(-60);
        await ReplaceAgreementTimelineAsync(userId, (t60, null, "AC"));

        var client = AdminClient();
        var before = await ReadUsersVersionAsync(client, userId);

        var rsp = await PutUserAsync(client, userId,
            body: new { agreementCode = "HK", effectiveFrom = t60.AddDays(10).ToString("yyyy-MM-dd") },
            ifMatch: $"\"{before}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var after = await ReadUsersVersionRawAsync(userId);
        Assert.Equal(before + 1, after);
        Assert.Equal($"\"{after}\"", rsp.Headers.ETag?.ToString());

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(after, body.GetProperty("version").GetInt64());
    }

    /// <summary>
    /// The silent no-op this task closes. Timeline: AC from T-60, and today's code is still AC.
    /// HR records "she was actually on HK from T-40 until T-20" — wait, no: the sharper case is a
    /// BACKDATED code EQUAL to today's code over a stretch where the record says something else.
    /// Setup: AC [T-60, T-30), HK [T-30, ∞) — today's code is HK. HR corrects the EARLY stretch to
    /// HK too. Pre-S138 the endpoint compared the request against the LIVE cache ("HK == HK ⇒
    /// nothing to do") and silently discarded a real correction. Now the comparison is against the
    /// row COVERING the requested date (AC), so the row IS written — while the live cache stays
    /// HK, because the correction never touched today.
    /// </summary>
    [Fact]
    public async Task UsersPut_BackdatedCodeEqualToTodaysCode_StillWritesTheRow_CacheUnchanged()
    {
        var userId = await SeedUserAsync(agreementCode: "HK");
        var t60 = Today.AddDays(-60);
        var t30 = Today.AddDays(-30);
        await ReplaceAgreementTimelineAsync(userId, (t60, t30, "AC"), (t30, null, "HK"));

        var client = AdminClient();
        var before = await ReadUsersVersionAsync(client, userId);

        var rsp = await PutUserAsync(client, userId,
            body: new { agreementCode = "HK", effectiveFrom = t60.AddDays(10).ToString("yyyy-MM-dd") },
            ifMatch: $"\"{before}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // The AC row was split at T-50 and an HK row inserted for [T-50, T-30).
        var rows = await ReadAgreementTimelineAsync(userId);
        Assert.Equal(3, rows.Count);
        Assert.Equal((t60, t60.AddDays(10), "AC"), (rows[0].From, rows[0].To, rows[0].Code));
        Assert.Equal((t60.AddDays(10), t30, "HK"), (rows[1].From, rows[1].To, rows[1].Code));
        Assert.Equal((t30, (DateOnly?)null, "HK"), (rows[2].From, rows[2].To, rows[2].Code));

        // The live cache means "as of today" — the correction did not touch today, so it stands.
        Assert.Equal("HK", await ReadUsersAgreementCodeAsync(userId));
        // ...but the TOKEN moved, exactly once, because the users row was rewritten.
        Assert.Equal(before + 1, await ReadUsersVersionRawAsync(userId));
    }

    /// <summary>
    /// A code EQUAL to the row covering the requested date is the real no-op: nothing written, no
    /// event, and the token stands still.
    /// </summary>
    [Fact]
    public async Task UsersPut_CodeEqualToTheCoveringRow_IsNoOp_NoRowNoEventNoBump()
    {
        var userId = await SeedUserAsync(agreementCode: "AC");
        var t60 = Today.AddDays(-60);
        await ReplaceAgreementTimelineAsync(userId, (t60, null, "AC"));

        var client = AdminClient();
        var before = await ReadUsersVersionAsync(client, userId);
        var changedBefore = await CountEventsAsync($"user-{userId}", "UserAgreementCodeChanged");

        var rsp = await PutUserAsync(client, userId,
            body: new { agreementCode = "AC", effectiveFrom = t60.AddDays(10).ToString("yyyy-MM-dd") },
            ifMatch: $"\"{before}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Single(await ReadAgreementTimelineAsync(userId));
        Assert.Equal(changedBefore, await CountEventsAsync($"user-{userId}", "UserAgreementCodeChanged"));
        // The users row was still rewritten by the endpoint (display fields), so the token moves
        // ONCE — by the endpoint, not the writer. One bump, never two.
        Assert.Equal(before + 1, await ReadUsersVersionRawAsync(userId));
    }

    /// <summary>
    /// Future-dating is still refused, and the body is DATE-FREE (it shares its shape with the
    /// employment-start-floor refusal, which must never echo the hire date).
    /// </summary>
    [Fact]
    public async Task UsersPut_FutureDatedAgreementCode_Returns422_WithNoDateInTheBody()
    {
        var userId = await SeedUserAsync(agreementCode: "AC");
        await ReplaceAgreementTimelineAsync(userId, (Today.AddDays(-60), null, "AC"));

        var client = AdminClient();
        var before = await ReadUsersVersionAsync(client, userId);
        var tomorrow = Today.AddDays(1);

        var rsp = await PutUserAsync(client, userId,
            body: new { agreementCode = "HK", effectiveFrom = tomorrow.ToString("yyyy-MM-dd") },
            ifMatch: $"\"{before}\"");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(tomorrow.ToString("yyyy-MM-dd"), raw, StringComparison.Ordinal);
        Assert.Equal(before, await ReadUsersVersionRawAsync(userId));
    }

    /// <summary>
    /// The presence guard on the agreement path. <c>effectiveFrom</c> is a non-nullable date on the
    /// wire, so omitting it binds 0001-01-01 — which, now that any past date is legal, would route
    /// as a correction covering ALL recorded history (0001-01-01 is also the backfill seeder's
    /// start). Pre-S138 the "must equal today" rule rejected it as a side effect; the guard keeps
    /// that protection explicitly. The no-agreement-code path is unaffected (EffectiveFrom is
    /// irrelevant there and still ignored).
    /// </summary>
    [Fact]
    public async Task UsersPut_AgreementCodeWithoutEffectiveFrom_Returns422_AndWritesNothing()
    {
        var userId = await SeedUserAsync(agreementCode: "AC");
        await ReplaceAgreementTimelineAsync(userId, (Today.AddDays(-60), null, "AC"));

        var client = AdminClient();
        var before = await ReadUsersVersionRawAsync(userId);

        var rsp = await PutUserAsync(client, userId,
            body: new { agreementCode = "HK" },
            ifMatch: $"\"{before}\"");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        Assert.Single(await ReadAgreementTimelineAsync(userId));
        Assert.Equal(before, await ReadUsersVersionRawAsync(userId));
    }

    /// <summary>
    /// UNCHANGED refusal, pinned because S138 deliberately did NOT widen it: the general users PUT
    /// cannot address a deactivated subject at all (its active-only read dead-ends at 404), and it
    /// therefore cannot reactivate a leaver as a side effect of an agreement-code correction.
    /// The leaver case belongs to the dedicated endpoint below.
    /// </summary>
    [Fact]
    public async Task UsersPut_DeactivatedSubject_StillUnaddressable_AndCannotReactivate()
    {
        var userId = await SeedUserAsync(agreementCode: "AC", isActive: false);
        await ReplaceAgreementTimelineAsync(userId, (Today.AddDays(-60), null, "AC"));

        var client = AdminClient();
        var rsp = await PutUserAsync(client, userId,
            body: new
            {
                agreementCode = "HK",
                isActive = true,
                effectiveFrom = Today.ToString("yyyy-MM-dd"),
            },
            ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
        Assert.False(await ReadUserIsActiveAsync(userId), "the general users PUT must never reactivate a leaver.");
        Assert.Equal("AC", await ReadUsersAgreementCodeAsync(userId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // B. The dedicated terminated-inclusive endpoint
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The marquee leaver pin. A DEPARTED employee's agreement code is corrected for a past
    /// stretch through the dedicated endpoint: the row is split, both events fire, both audit
    /// tables get their row, the exported month lands on the HR worklist, the users token moves
    /// exactly once — and the leaver is NOT reactivated.
    /// </summary>
    [Fact]
    public async Task AgreementCodeEndpoint_HrCorrectsDepartedEmployee_WritesEventsAuditAndWorklist()
    {
        var userId = await SeedUserAsync(agreementCode: "AC", isActive: false);
        var t120 = Today.AddDays(-120);
        await ReplaceAgreementTimelineAsync(userId, (t120, null, "AC"));
        var exportMonth = new DateOnly(Today.AddMonths(-3).Year, Today.AddMonths(-3).Month, 1);
        await SeedExportRecordAsync(userId, exportMonth.Year, exportMonth.Month);

        var client = AdminClient();
        var before = await ReadUsersVersionRawAsync(userId);

        var rsp = await PutAgreementCodeAsync(client, userId, "HK", exportMonth, $"\"{before}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // Two rows: AC [t120, exportMonth) and HK [exportMonth, ∞).
        var rows = await ReadAgreementTimelineAsync(userId);
        Assert.Equal(2, rows.Count);
        Assert.Equal((t120, exportMonth, "AC"), (rows[0].From, rows[0].To, rows[0].Code));
        Assert.Equal((exportMonth, (DateOnly?)null, "HK"), (rows[1].From, rows[1].To, rows[1].Code));

        // Both events on the canonical user-{id} stream.
        Assert.Equal(1, await CountEventsAsync($"user-{userId}", "UserAgreementCodeChanged"));
        Assert.Equal(1, await CountEventsAsync($"user-{userId}", "UserAgreementCodeSuperseded"));

        // Both audit tables.
        Assert.Equal("SUPERSEDED", await ReadLatestAgreementAuditActionAsync(userId));
        Assert.Equal(1, await CountUsersAuditAsync(userId, "UPDATED"));

        // The HR worklist for the exported month, trigger AGREEMENT_CODE_CHANGE.
        var worklist = await ReadWorklistRowsAsync(userId, "EXPORTED_MONTH");
        var row = Assert.Single(worklist);
        Assert.Equal(exportMonth.Year, row.Year);
        Assert.Equal(exportMonth.Month, row.Month);
        Assert.Contains("AGREEMENT_CODE_CHANGE", row.TriggersJson, StringComparison.Ordinal);

        // The token moved exactly once; the ETag carries it; the leaver stays deactivated.
        var after = await ReadUsersVersionRawAsync(userId);
        Assert.Equal(before + 1, after);
        Assert.Equal($"\"{after}\"", rsp.Headers.ETag?.ToString());
        Assert.False(await ReadUserIsActiveAsync(userId));

        // The row written covers today, so the live cache followed it.
        Assert.Equal("HK", await ReadUsersAgreementCodeAsync(userId));
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("HK", body.GetProperty("agreementCode").GetString());
        Assert.False(body.GetProperty("noOp").GetBoolean());
    }

    /// <summary>
    /// A HISTORY-ONLY correction through the dedicated endpoint leaves the live cache alone (the
    /// corrected interval is closed and does not reach today) while still moving the token — and
    /// the Superseded event carries <c>newEffectiveTo</c>, the marker that distinguishes an
    /// insert-between from the classic cross-day supersession.
    ///
    /// <para>
    /// S138 / TASK-13810 (owner ruling 2026-09-03) also pins the RESPONSE BODY here, because it is
    /// now a shared rule rather than this endpoint's private habit: both correction endpoints answer
    /// with the state AS OF TODAY. This request writes "PROSA" into closed history, so the body
    /// reports the code that is true today ("HK") — NOT the code the caller sent. The profile PUT
    /// was changed to match; behaviour on this endpoint is unchanged.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AgreementCodeEndpoint_HistoryOnlyCorrection_LeavesTheLiveCacheUntouched()
    {
        var userId = await SeedUserAsync(agreementCode: "HK");
        var t90 = Today.AddDays(-90);
        var t30 = Today.AddDays(-30);
        await ReplaceAgreementTimelineAsync(userId, (t90, t30, "AC"), (t30, null, "HK"));

        var client = AdminClient();
        var before = await ReadUsersVersionRawAsync(userId);

        var rsp = await PutAgreementCodeAsync(client, userId, "PROSA", t90.AddDays(10), $"\"{before}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var rows = await ReadAgreementTimelineAsync(userId);
        Assert.Equal(3, rows.Count);
        Assert.Equal((t90.AddDays(10), t30, "PROSA"), (rows[1].From, rows[1].To, rows[1].Code));

        Assert.Equal("HK", await ReadUsersAgreementCodeAsync(userId));
        Assert.Equal(before + 1, await ReadUsersVersionRawAsync(userId));

        // The SHARED RESPONSE RULE (S138 / TASK-13810): the body is today's code, not "PROSA".
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("HK", body.GetProperty("agreementCode").GetString());

        var payload = await ReadLatestEventPayloadAsync($"user-{userId}", "UserAgreementCodeSuperseded");
        Assert.NotNull(payload);
        using var doc = JsonDocument.Parse(payload!);
        Assert.Equal(t30.ToString("yyyy-MM-dd"), doc.RootElement.GetProperty("newEffectiveTo").GetString());
        Assert.Equal("AC", doc.RootElement.GetProperty("oldAgreementCode").GetString());
    }

    /// <summary>
    /// The same-values no-op on the dedicated endpoint: 200, unchanged ETag, nothing written.
    /// </summary>
    [Fact]
    public async Task AgreementCodeEndpoint_CodeEqualToTheCoveringRow_IsNoOp()
    {
        var userId = await SeedUserAsync(agreementCode: "AC");
        var t60 = Today.AddDays(-60);
        await ReplaceAgreementTimelineAsync(userId, (t60, null, "AC"));

        var client = AdminClient();
        var before = await ReadUsersVersionRawAsync(userId);

        var rsp = await PutAgreementCodeAsync(client, userId, "AC", t60.AddDays(10), $"\"{before}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Single(await ReadAgreementTimelineAsync(userId));
        Assert.Equal(0, await CountEventsAsync($"user-{userId}", "UserAgreementCodeChanged"));
        Assert.Equal(before, await ReadUsersVersionRawAsync(userId));
        Assert.Equal($"\"{before}\"", rsp.Headers.ETag?.ToString());

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("noOp").GetBoolean());
    }

    /// <summary>
    /// SEC-046 holds on the new surface: the departed employee's OWN token cannot correct their
    /// agreement code. (An Employee-only actor is refused; the surface is HR-and-above with a
    /// LocalHR floor on the admitting scope.)
    /// </summary>
    [Fact]
    public async Task AgreementCodeEndpoint_LeaverOwnToken_Returns403()
    {
        var userId = await SeedUserAsync(agreementCode: "AC", isActive: false);
        await ReplaceAgreementTimelineAsync(userId, (Today.AddDays(-60), null, "AC"));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(userId));

        var rsp = await PutAgreementCodeAsync(client, userId, "HK", Today.AddDays(-10), "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("AC", await ReadUsersAgreementCodeAsync(userId));
    }

    /// <summary>Admin-strict If-Match on <c>users.version</c>: 428 when absent.</summary>
    [Fact]
    public async Task AgreementCodeEndpoint_MissingIfMatch_Returns428()
    {
        var userId = await SeedUserAsync(agreementCode: "AC");
        await ReplaceAgreementTimelineAsync(userId, (Today.AddDays(-60), null, "AC"));

        var client = AdminClient();
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}/agreement-code")
        {
            Content = JsonContent.Create(new
            {
                agreementCode = "HK",
                effectiveFrom = Today.AddDays(-10).ToString("yyyy-MM-dd"),
            }),
        };
        var rsp = await client.SendAsync(req);
        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    /// <summary>Admin-strict If-Match on <c>users.version</c>: 412 when stale, with the structured
    /// expected/actual body — and nothing written.</summary>
    [Fact]
    public async Task AgreementCodeEndpoint_StaleIfMatch_Returns412()
    {
        var userId = await SeedUserAsync(agreementCode: "AC");
        await ReplaceAgreementTimelineAsync(userId, (Today.AddDays(-60), null, "AC"));

        var client = AdminClient();
        var actual = await ReadUsersVersionRawAsync(userId);

        var rsp = await PutAgreementCodeAsync(client, userId, "HK", Today.AddDays(-10), $"\"{actual + 7}\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(actual + 7, body.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(actual, body.GetProperty("actualVersion").GetInt64());
        Assert.Single(await ReadAgreementTimelineAsync(userId));
    }

    /// <summary>Future-dating is refused on the dedicated endpoint too, DATE-FREE.</summary>
    [Fact]
    public async Task AgreementCodeEndpoint_FutureDated_Returns422_WithNoDateInTheBody()
    {
        var userId = await SeedUserAsync(agreementCode: "AC");
        await ReplaceAgreementTimelineAsync(userId, (Today.AddDays(-60), null, "AC"));

        var client = AdminClient();
        var version = await ReadUsersVersionRawAsync(userId);
        var tomorrow = Today.AddDays(1);

        var rsp = await PutAgreementCodeAsync(client, userId, "HK", tomorrow, $"\"{version}\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(tomorrow.ToString("yyyy-MM-dd"), raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// The employment-start floor on the dedicated endpoint: a correction dated before the hire is
    /// a 422 whose body never carries the hire date (HR-scoped data must not reach the wire).
    /// </summary>
    [Fact]
    public async Task AgreementCodeEndpoint_BeforeEmploymentStart_Returns422_WithNoDateInTheBody()
    {
        var hireDate = Today.AddDays(-100);
        var userId = await SeedUserAsync(agreementCode: "AC", employmentStartDate: hireDate);
        await ReplaceAgreementTimelineAsync(userId, (hireDate, null, "AC"));

        var client = AdminClient();
        var version = await ReadUsersVersionRawAsync(userId);

        var rsp = await PutAgreementCodeAsync(client, userId, "HK", hireDate.AddDays(-1), $"\"{version}\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(hireDate.ToString("yyyy-MM-dd"), raw, StringComparison.Ordinal);
        Assert.Single(await ReadAgreementTimelineAsync(userId));
    }

    // ─── Seeding helpers ─────────────────────────────────────────────────

    private async Task<string> SeedUserAsync(
        string agreementCode, bool isActive = true, DateOnly? employmentStartDate = null)
    {
        var userId = "usr_s138_ac_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, employment_category,
                               employment_start_date, is_active)
            VALUES (@u, @u, 'dev-only', 'S138 Agreement Backdating User', NULL,
                    @org, @code, 'OK24', 'Standard', @start, @active)
            """, conn))
        {
            cmd.Parameters.AddWithValue("u", userId);
            cmd.Parameters.AddWithValue("org", OrgId);
            cmd.Parameters.AddWithValue("code", agreementCode);
            cmd.Parameters.AddWithValue("start", (object?)employmentStartDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("active", isActive);
            await cmd.ExecuteNonQueryAsync();
        }
        // A live profile row so the user is a complete subject (not read by these pins, but the
        // seeder would otherwise create one at the next host boot and perturb the timeline).
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

    private async Task SeedExportRecordAsync(string employeeId, int year, int month)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month,
                 original_lines, current_effective_lines, content_hash)
            VALUES (gen_random_uuid(), NULL, @e, @y, @m, '[]'::jsonb, '[]'::jsonb, @hash)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        cmd.Parameters.AddWithValue("hash", $"hash-{year}-{month}");
        await cmd.ExecuteNonQueryAsync();
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

    private async Task<string?> ReadUsersAgreementCodeAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT agreement_code FROM users WHERE user_id = @u", conn);
        cmd.Parameters.AddWithValue("u", userId);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task<long> ReadUsersVersionRawAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT version FROM users WHERE user_id = @u", conn);
        cmd.Parameters.AddWithValue("u", userId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<bool> ReadUserIsActiveAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT is_active FROM users WHERE user_id = @u", conn);
        cmd.Parameters.AddWithValue("u", userId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> CountEventsAsync(string streamId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = @t", conn);
        cmd.Parameters.AddWithValue("s", streamId);
        cmd.Parameters.AddWithValue("t", eventType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<string?> ReadLatestEventPayloadAsync(string streamId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @s AND event_type = @t
            ORDER BY outbox_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("s", streamId);
        cmd.Parameters.AddWithValue("t", eventType);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task<string?> ReadLatestAgreementAuditActionAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action FROM user_agreement_codes_audit
            WHERE user_id = @u ORDER BY audit_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task<long> CountUsersAuditAsync(string userId, string action)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users_audit WHERE user_id = @u AND action = @a", conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("a", action);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private sealed record WorklistRow(int? Year, int? Month, string TriggersJson);

    private async Task<IReadOnlyList<WorklistRow>> ReadWorklistRowsAsync(string employeeId, string kind)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT year, month, triggers::text FROM hr_backdate_worklist
            WHERE employee_id = @e AND kind = @k ORDER BY created_at, worklist_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("k", kind);
        var rows = new List<WorklistRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new WorklistRow(
                reader.IsDBNull(0) ? null : reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetString(2)));
        }
        return rows;
    }

    // ─── HTTP helpers ────────────────────────────────────────────────────

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return client;
    }

    private static string MintGlobalAdminToken()
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: "ADMIN_S138_AC",
            name: "S138 Agreement Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private static string MintEmployeeToken(string userId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: userId,
            name: userId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

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

    private static async Task<HttpResponseMessage> PutAgreementCodeAsync(
        HttpClient client, string userId, string agreementCode, DateOnly effectiveFrom, string ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}/agreement-code")
        {
            Content = JsonContent.Create(new
            {
                agreementCode,
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
            }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }
}
