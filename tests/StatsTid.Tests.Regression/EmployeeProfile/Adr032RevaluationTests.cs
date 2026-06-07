using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// S66 / TASK-6607 / ADR-032 D4 — profile-change revaluation pins. When a profile PUT changes a
/// fullDayHours-affecting field (part_time_fraction OR position), every entitlement-consuming
/// absence dated ≥ effectiveFrom (= today, ADR-023 D8) is revalued IN THE SAME TX: the per-absence
/// <c>absences_projection.feriedage</c> is replaced, <c>entitlement_balances.used</c> is adjusted
/// (ungated — may go past the cap), and one <c>EntitlementBalanceRevalued</c> event per
/// (type, year) lands on the consolidated <c>employee-{id}</c> stream with an ADR-026 audit row.
/// Past-dated absences are untouched; the ETag / If-Match flow is byte-identical.
///
/// <para><b>today-dependence (FixedTimeProvider does NOT help here).</b> The PUT validator narrows
/// <c>EffectiveFrom</c> to <c>DateOnly.FromDateTime(DateTime.UtcNow)</c> directly (NOT via
/// <c>TimeProvider</c>), so the revaluation window is real-today-anchored. The fixtures therefore
/// book "future" absences on the next few real weekdays (≥ today) so they fall inside the
/// revaluation window, and a "past" absence the day before today. This mirrors the existing
/// <see cref="EmployeeProfileLifecycleTests"/> convention (it uses <c>DateOnly.FromDateTime(
/// DateTime.UtcNow)</c> for the same PUT path) — no wall-clock-dependent EXPECTED VALUES are
/// asserted, only the revaluation invariants (replacement happened / past untouched / event emitted).</para>
///
/// <para>The booking path is the rule-stubbed Skema save (so absences carry recorded feriedage);
/// the PUT is the admin <c>/api/admin/employee-profiles/{id}</c> endpoint (GlobalAdmin token +
/// admin-strict If-Match). The Skema save needs a rule-engine stub; the admin PUT does not — both
/// ride ONE rule-stubbed host so the stub is present for the save.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class Adr032RevaluationTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private HttpClient _ruleStubbedClient = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient();
        // ONE rule-stubbed host shared across saves + PUT in this suite.
        _ruleStubbedClient = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory());
            });
        }).CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Next weekday strictly after <paramref name="from"/> (skips Sat/Sun) — keeps bookings
    /// on positive-norm days so the per-day guard passes.</summary>
    private static DateOnly NextWeekday(DateOnly from)
    {
        var d = from.AddDays(1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            d = d.AddDays(1);
        return d;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Fraction-change revaluation — the marquee D4 pin.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A PUT changing part_time_fraction (1.0 → 0.5) revalues a FUTURE-dated VACATION absence:
    /// the recorded <c>absences_projection.feriedage</c> doubles (7.4h was 1.0 feriedag at the
    /// full-time norm 7.4; at the new half-time norm 3.7 it is 2.0), an
    /// <c>EntitlementBalanceRevalued</c> event lands on <c>employee-{id}</c> with the replacement
    /// set, <c>used</c> is adjusted by +1.0, and a PAST-dated absence is left untouched. The PUT's
    /// 200 + new ETag are unchanged.
    /// </summary>
    [Fact]
    public async Task FractionChange_RevaluesFutureAbsence_PastUntouched_EmitsEventAndAudit()
    {
        var employeeId = await SeedFullTimeEmployeeAsync();

        // A future-dated VACATION (≥ today) booked under the FULL-TIME profile → recorded 1.0.
        var futureDay = NextWeekday(Today);
        await BookVacationAsync(employeeId, futureDay, 7.4m);
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, futureDay)); // 7.4 / 7.4 = 1.0 at full-time

        // A PAST-dated VACATION (seeded directly into the projection — it predates today and could
        // not be booked via the future-only window). Recorded feriedage 1.0; must stay 1.0.
        var pastDay = Today.AddDays(-30);
        var pastFeriedageBefore = 1.0m;
        await SeedPastAbsenceProjectionRowAsync(employeeId, pastDay, "VACATION", 7.4m, pastFeriedageBefore);

        // PUT: full-time → half-time, effectiveFrom = today (validator-narrowed).
        var adminClient = AdminClient();
        var version = await ReadProfileVersionAsync(adminClient, employeeId);
        var putRsp = await PutProfileAsync(adminClient, employeeId, Today, 0.500m, position: null, ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, putRsp.StatusCode); // ETag flow unchanged

        // Future absence revalued: 7.4 / 3.7 = 2.0 (was 1.0).
        Assert.Equal(2.0m, await ReadFeriedageAsync(employeeId, futureDay));
        // Past absence untouched.
        Assert.Equal(pastFeriedageBefore, await ReadFeriedageAsync(employeeId, pastDay));

        // EntitlementBalanceRevalued event on the employee-{id} stream carrying the replacement set.
        // ADR-032 D4 + the EntitlementBalanceRevalued xmldoc both mandate the CONSOLIDATED
        // `employee-{employeeId}` stream (ADR-018 D6 balance-event lineage). The failure message
        // names the stream the event ACTUALLY landed on so a stream-routing defect is unambiguous.
        var actualStream = await FindRevaluedEventStreamAsync(employeeId);
        var (revaluedCount, replacementForFuture) = await ReadRevaluedReplacementAsync(employeeId, futureDay, "VACATION");
        Assert.True(revaluedCount >= 1,
            $"ADR-032 D4: expected an EntitlementBalanceRevalued event on stream 'employee-{employeeId}', " +
            $"but found none there. Actual stream(s) carrying the event: [{actualStream}].");
        Assert.Equal(2.0m, replacementForFuture); // the replacement set carries the new per-absence value

        // ADR-026 audit_projection row for the revaluation event.
        Assert.True(await CountAuditProjectionAsync(employeeId, "EntitlementBalanceRevalued") >= 1,
            "Expected an audit_projection row for EntitlementBalanceRevalued (ADR-026 mapper).");

        // used adjusted by +1.0 (future absence only: 1.0 → 2.0). VACATION entitlement_year for a
        // future weekday: reset month 9 ⇒ year = (month ≥ 9 ? year : year−1); the PAST absence is in
        // its own ferieår and was NOT revalued, so only the future group's delta lands.
        // We assert the future group's used reflects the +1.0 revaluation delta on top of its booked 1.0.
        var futureYear = futureDay.Month >= 9 ? futureDay.Year : futureDay.Year - 1;
        var (_, usedFuture) = await ReadBalanceAsync(employeeId, "VACATION", futureYear);
        Assert.Equal(2.0m, usedFuture); // booked 1.0 + revaluation delta +1.0
    }

    // ════════════════════════════════════════════════════════════════════════
    // Revaluation past the cap — negative remaining tolerated (no 500).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-032 D4: revaluation may push <c>used</c> past the entitlement cap — it is the UNGATED
    /// path (not the booking guard). A near-cap full-timer (booked 25 future VACATION days) whose
    /// fraction halves doubles every recorded feriedag (→ 50 used, far past the 25 cap). The PUT
    /// still succeeds (200), never 500, never clamps. <c>used</c> lands at 50.
    /// </summary>
    [Fact]
    public async Task FractionChange_RevaluationPastCap_Succeeds_NoClampNo500()
    {
        var employeeId = await SeedFullTimeEmployeeAsync();

        // Book 24 future VACATION days WITHIN a single ferieår (≤ the 25-day forskud cap for a
        // full-timer). To keep them all in ONE ferieår regardless of today, only book days that
        // share the firstAbsenceDate's ferieår (reset month 9): stop at the next Sep-1 boundary.
        var firstDay = NextWeekday(Today);
        var firstYear = firstDay.Month >= 9 ? firstDay.Year : firstDay.Year - 1;
        var ferieaarEndExclusive = new DateOnly(firstYear + 1, 9, 1); // next Sep-1
        var day = firstDay;
        var booked = 0;
        while (booked < 24 && day < ferieaarEndExclusive)
        {
            await BookVacationAsync(employeeId, day, 7.4m); // 1.0 each at full-time
            booked++;
            day = NextWeekday(day);
        }
        Assert.True(booked >= 1, "Expected to book at least one future VACATION day in the ferieår.");

        var (_, usedBefore) = await ReadBalanceAsync(employeeId, "VACATION", firstYear);
        Assert.Equal((decimal)booked, usedBefore); // each booked day consumed 1.0 at full-time

        var adminClient = AdminClient();
        var version = await ReadProfileVersionAsync(adminClient, employeeId);
        var putRsp = await PutProfileAsync(adminClient, employeeId, Today, 0.500m, position: null, ifMatch: $"\"{version}\"");

        // The PUT succeeds despite pushing used past the 25 cap (ungated revaluation, ADR-032 D4).
        Assert.Equal(HttpStatusCode.OK, putRsp.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, putRsp.StatusCode);

        var (_, usedAfter) = await ReadBalanceAsync(employeeId, "VACATION", firstYear);
        // Every booked day in this ferieår doubled (1.0 → 2.0). used = 2 × usedBefore.
        Assert.Equal(usedBefore * 2m, usedAfter);
        // For the past-cap intent we need ≥ 13 booked days so 2× exceeds the 25-day cap. If the
        // ferieår tail was too short to book 13 (today within ~13 weekdays of Sep 1), the doubling
        // is still pinned above; the strict past-cap assertion only fires when representable.
        if (booked >= 13)
            Assert.True(usedAfter > 25m, $"Revaluation must be allowed past the 25-day cap; used={usedAfter}.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // No-change PUT — ETag flow byte-identical, no revaluation.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-032 D4: a PUT that changes NEITHER fraction NOR position triggers NO revaluation — the
    /// recorded feriedage is unchanged and NO EntitlementBalanceRevalued event is emitted. The
    /// If-Match / ETag flow is byte-identical (200 + bumped version). Pins the "zero new behavior
    /// when nothing fullDayHours-affecting changed" contract.
    /// </summary>
    [Fact]
    public async Task NoFullDayHoursAffectingChange_NoRevaluation_EtagFlowUnchanged()
    {
        var employeeId = await SeedFullTimeEmployeeAsync();
        var futureDay = NextWeekday(Today);
        await BookVacationAsync(employeeId, futureDay, 7.4m);
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, futureDay));

        var adminClient = AdminClient();
        var version = await ReadProfileVersionAsync(adminClient, employeeId);
        // Re-state the SAME fraction (1.0) and SAME position (null) — no fullDayHours-affecting change.
        var putRsp = await PutProfileAsync(adminClient, employeeId, Today, 1.000m, position: null, ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, putRsp.StatusCode);

        // No revaluation: feriedage unchanged, no event.
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, futureDay));
        var (revaluedCount, _) = await ReadRevaluedReplacementAsync(employeeId, futureDay, "VACATION");
        Assert.Equal(0, revaluedCount);
    }

    // ── Seeding / booking ──

    private async Task<string> SeedFullTimeEmployeeAsync()
    {
        var employeeId = "emp_s66_reval_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        await using (var userCmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@u, @u, 'dev-only', 'S66 Revaluation Test User', NULL, @org, 'AC', 'OK24', TRUE)
            """, conn))
        {
            userCmd.Parameters.AddWithValue("u", employeeId);
            userCmd.Parameters.AddWithValue("org", OrgId);
            await userCmd.ExecuteNonQueryAsync();
        }

        // Live OPEN full-time profile at effective_from = today so the admin PUT routes Case B
        // (same-day in-place edit) — the validator requires EffectiveFrom == today, and a same-day
        // predecessor keeps the test independent of the seeder's effective_from convention.
        await using (var profileCmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (profile_id, employee_id, part_time_fraction, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @e, 1.000, @today, NULL, 1)
            ON CONFLICT (employee_id, effective_from) DO UPDATE SET part_time_fraction = EXCLUDED.part_time_fraction
            """, conn))
        {
            profileCmd.Parameters.AddWithValue("e", employeeId);
            profileCmd.Parameters.AddWithValue("today", Today);
            await profileCmd.ExecuteNonQueryAsync();
        }

        await using (var agreementCmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, 'AC', '0001-01-01', NULL, 1)
            ON CONFLICT (user_id, effective_from) DO UPDATE SET agreement_code = EXCLUDED.agreement_code
            """, conn))
        {
            agreementCmd.Parameters.AddWithValue("u", employeeId);
            await agreementCmd.ExecuteNonQueryAsync();
        }

        return employeeId;
    }

    /// <summary>Books a single VACATION absence via the rule-stubbed Skema save (so it carries
    /// recorded feriedage from the live consumption valuation).</summary>
    private async Task BookVacationAsync(string employeeId, DateOnly date, decimal hours)
    {
        var client = _ruleStubbedClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId, OrgId));
        var rsp = await client.PostAsJsonAsync($"/api/skema/{employeeId}/save", new
        {
            year = date.Year,
            month = date.Month,
            absences = new[]
            {
                new { date = date.ToString("yyyy-MM-dd"), absenceType = "VACATION", hours },
            },
        });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    /// <summary>Directly seeds a PAST-dated absence projection row with a known feriedage (the past
    /// window cannot be booked through the future-only save path).</summary>
    private async Task SeedPastAbsenceProjectionRowAsync(
        string employeeId, DateOnly date, string absenceType, decimal hours, decimal feriedage)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage,
                 agreement_code, ok_version, occurred_at, actor_id, actor_role, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, @type, @hours, @feriedage,
                 'AC', 'OK24', NOW(), 'test-seed', 'Employee', -1)
            ON CONFLICT (event_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("type", absenceType);
        cmd.Parameters.AddWithValue("hours", hours);
        cmd.Parameters.AddWithValue("feriedage", feriedage);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Reads ──

    private async Task<decimal> ReadFeriedageAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT feriedage FROM absences_projection WHERE employee_id = @e AND date = @d", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", date);
        var result = await cmd.ExecuteScalarAsync();
        Assert.False(result is null or DBNull, $"Expected a non-null feriedage for {date}.");
        return (decimal)result!;
    }

    /// <summary>Returns (revaluedEventCount, newFeriedageForTargetAbsence) by reading the
    /// EntitlementBalanceRevalued events on the employee-{id} stream and finding the replacement
    /// whose absence event_id matches the target (employee, date, type) projection row.</summary>
    private async Task<(int Count, decimal? NewFeriedage)> ReadRevaluedReplacementAsync(
        string employeeId, DateOnly targetDate, string targetEntitlementType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // The absence event_id for the target projection row.
        Guid? targetEventId = null;
        await using (var idCmd = new NpgsqlCommand(
            "SELECT event_id FROM absences_projection WHERE employee_id = @e AND date = @d", conn))
        {
            idCmd.Parameters.AddWithValue("e", employeeId);
            idCmd.Parameters.AddWithValue("d", targetDate);
            var r = await idCmd.ExecuteScalarAsync();
            if (r is Guid g) targetEventId = g;
        }

        var count = 0;
        decimal? newFeriedage = null;
        // Read the SYNC-written outbox_events.event_payload (the canonical `events` table is
        // populated later by the async OutboxPublisher — reading it here would race the drain).
        await using (var evCmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @s AND event_type = 'EntitlementBalanceRevalued'
            ORDER BY outbox_id ASC
            """, conn))
        {
            evCmd.Parameters.AddWithValue("s", $"employee-{employeeId}");
            await using var reader = await evCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                count++;
                using var doc = JsonDocument.Parse(reader.GetString(0));
                var type = doc.RootElement.GetProperty("entitlementType").GetString();
                if (type != targetEntitlementType) continue;
                foreach (var repl in doc.RootElement.GetProperty("replacements").EnumerateArray())
                {
                    if (targetEventId is { } te
                        && repl.GetProperty("absenceEventId").GetGuid() == te)
                    {
                        newFeriedage = repl.GetProperty("newFeriedage").GetDecimal();
                    }
                }
            }
        }
        return (count, newFeriedage);
    }

    /// <summary>Diagnostic: the distinct stream_id(s) on which an EntitlementBalanceRevalued event
    /// for this employee actually landed (matched by the employeeId inside the payload, stream-
    /// agnostic). Used only to make a stream-routing-defect failure message precise.</summary>
    private async Task<string> FindRevaluedEventStreamAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT DISTINCT stream_id FROM outbox_events
            WHERE event_type = 'EntitlementBalanceRevalued'
              AND (event_payload ->> 'employeeId') = @e
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        var streams = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            streams.Add(reader.GetString(0));
        return streams.Count == 0 ? "(none)" : string.Join(", ", streams);
    }

    private async Task<long> CountAuditProjectionAsync(string employeeId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = @t", conn);
        cmd.Parameters.AddWithValue("t", eventType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<(decimal TotalQuota, decimal Used)> ReadBalanceAsync(
        string employeeId, string entitlementType, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT total_quota, used FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Expected an entitlement_balances row for {entitlementType}/{year}.");
        return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    private async Task<long> ReadProfileVersionAsync(HttpClient adminClient, string employeeId)
    {
        var rsp = await adminClient.GetAsync($"/api/admin/employee-profiles/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("version").GetInt64();
    }

    // ── HTTP helpers ──

    private HttpClient AdminClient()
    {
        var client = _ruleStubbedClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return client;
    }

    private static async Task<HttpResponseMessage> PutProfileAsync(
        HttpClient client, string employeeId, DateOnly effectiveFrom,
        decimal partTimeFraction, string? position, string ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{employeeId}")
        {
            Content = JsonContent.Create(new
            {
                effectiveFrom,
                partTimeFraction,
                position,
            }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    // ── Token minting ──

    private static string MintEmployeeToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static string MintGlobalAdminToken()
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: "ADMIN_S66_REVAL",
            name: "S66 Revaluation Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    // ── Rule-engine stub ──

    private sealed class RuleEngineStubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RuleEngineStubHandler(), disposeHandler: false);
    }

    private sealed class RuleEngineStubHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.EndsWith("/api/rules/validate-entitlement", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            var req = JsonSerializer.Deserialize<ValidateEntitlementRequest>(json, Camel)!;
            var result = EntitlementValidationRule.Evaluate(req);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(result, Camel), Encoding.UTF8, "application/json"),
            };
        }
    }
}
