using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// One Postgres container (the real <c>init.sql</c>) and one in-process Payroll host shared by
/// <see cref="RecalculateDepartedSelfTests"/>. Isolation is by case-unique employee ids.
/// </summary>
public sealed class DepartedPayrollFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private PayrollHostFactory _factory = null!;

    public string ConnectionString { get; private set; } = null!;
    public DbConnectionFactory Db { get; private set; } = null!;
    internal PayrollHostFactory Factory => _factory;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(ConnectionString);
        Db = new DbConnectionFactory(ConnectionString);
        await TestFixtures.SeedWageTypeMappingsAsync(Db);
        _factory = new PayrollHostFactory(ConnectionString, new RuleRegistry().GetAll());
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

/// <summary>
/// S147 / TASK-14703-T (AC-230-17, F1 — the Codex cycle-5 BLOCKER; P-4) — <c>POST /api/payroll/recalculate</c>
/// is <c>GlobalAdminOnly</c> with no organisation-scope check by design, so the validator's departed-self
/// clause never runs on it; the guard is inline. A GlobalAdmin who is ALSO an employee (their own
/// <c>users</c> row, an exported month) must not correct their OWN pay once they have departed.
///
/// <para><b>Seeded subject.</b> Still <c>is_active = TRUE</c> with an end date in the past (the real
/// grace state), plus a deactivated variant. The end date is the fixed 2026-03-04: the host reads the real
/// clock, and any real "today" after that date makes the subject departed.</para>
///
/// <para><b>Docker</b> — CI-pending, never locally green. Kinds and Red conditions are on each fact.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RecalculateDepartedSelfTests : IClassFixture<DepartedPayrollFixture>
{
    private const string OrgId = "STY_S147_RECALC";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";
    private const int Year = 2026;
    private const int Month = 3;
    private static readonly DateOnly Feb01 = new(2026, 2, 1);
    private static readonly DateOnly Feb28 = new(2026, 2, 28);
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);
    private static readonly DateOnly LastDay = new(2026, 3, 4);

    private static long _outboxSeq = 4_700_000;

    private readonly DepartedPayrollFixture _fx;
    public RecalculateDepartedSelfTests(DepartedPayrollFixture fx) => _fx = fx;

    // ═════════════════════════════════ the facts ═════════════════════════════════

    /// <summary>
    /// Kind F. A GlobalAdmin who is also an employee, ACTIVE with an end date in the past, posts
    /// <c>/recalculate</c> for their OWN exported month -> 403 with <see cref="DepartedSubject.DenyReason"/>,
    /// and nothing is written: no correction event, no audit row, <c>current_effective_lines</c> and the
    /// balance columns unchanged, no idempotency marker (a retry with the same token is not short-circuited).
    /// Red conditions: <c>Assert.Equal(403, status)</c> — today the route recalculates (200) because no guard
    /// exists; <c>Assert.Equal(0, correction events)</c> — today one event is written.
    /// </summary>
    [Fact]
    public async Task DepartedGlobalAdmin_OwnMonth_Recalculate_403_NothingWritten()
    {
        var self = "S147-GA-" + Guid.NewGuid().ToString("N")[..8];
        await SeedExportedMonthAsync(self, active: true, end: LastDay);
        var linesBefore = await ReadLinesAndBalancesAsync(self);
        var token = Guid.NewGuid();

        var rsp = await PostRecalculateAsync(self, self, token);
        var json = await rsp.Content.ReadAsStringAsync();

        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {json}");
        Assert.Contains(DepartedSubject.DenyReason, json);
        await AssertNothingWrittenAsync(self, token, linesBefore);
    }

    /// <summary>
    /// Kind F, deactivated variant. Same as above for a DEACTIVATED self (<c>is_active = FALSE</c>): the
    /// guard's read is terminated-inclusive, so a deactivated GlobalAdmin is refused too. Red conditions:
    /// the 403 and the zero correction events — today the route recalculates.
    /// </summary>
    [Fact]
    public async Task DeactivatedGlobalAdmin_OwnMonth_Recalculate_403_NothingWritten()
    {
        var self = "S147-GAD-" + Guid.NewGuid().ToString("N")[..8];
        await SeedExportedMonthAsync(self, active: false, end: LastDay);
        var linesBefore = await ReadLinesAndBalancesAsync(self);
        var token = Guid.NewGuid();

        var rsp = await PostRecalculateAsync(self, self, token);
        var json = await rsp.Content.ReadAsStringAsync();

        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {json}");
        Assert.Contains(DepartedSubject.DenyReason, json);
        await AssertNothingWrittenAsync(self, token, linesBefore);
    }

    /// <summary>
    /// Kind F (P-4). With an idempotency token ALREADY marked delivered for the month, the departed self
    /// still gets 403. Today <c>Payroll/Program.cs</c> answers 200 "Correction already processed" from the
    /// idempotency lookup BEFORE anything else, so this fact proves the guard sits BEFORE that lookup.
    /// Red condition: <c>Assert.Equal(403, status)</c> — today the answer is 200 with
    /// <c>message = "Correction already processed"</c>.
    /// </summary>
    [Fact]
    public async Task DepartedGlobalAdmin_OwnMonth_AlreadyDeliveredToken_Still403()
    {
        var self = "S147-GAT-" + Guid.NewGuid().ToString("N")[..8];
        await SeedExportedMonthAsync(self, active: true, end: LastDay);
        var token = Guid.NewGuid();
        await MarkTokenDeliveredAsync(token);

        var rsp = await PostRecalculateAsync(self, self, token);
        var json = await rsp.Content.ReadAsStringAsync();

        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {json}");
        Assert.Contains(DepartedSubject.DenyReason, json);
        Assert.DoesNotContain("Correction already processed", json);
    }

    /// <summary>
    /// Kind B — boundary pin, holds today. The SAME departed GlobalAdmin token recalculating ANOTHER
    /// employee's exported month is as today: 200 with <c>success = true</c>, exactly one correction event
    /// (the guard keys on the identity of the subject, not on the actor's state).
    /// </summary>
    [Fact]
    public async Task DepartedGlobalAdmin_OtherEmployeesMonth_AsToday_200()
    {
        var actor = "S147-GAO-" + Guid.NewGuid().ToString("N")[..8];
        var other = "S147-EMP-" + Guid.NewGuid().ToString("N")[..8];
        await SeedExportedMonthAsync(actor, active: true, end: LastDay);
        await SeedExportedMonthAsync(other, active: true, end: null);

        var rsp = await PostRecalculateAsync(actor, other, Guid.NewGuid());
        var json = await rsp.Content.ReadAsStringAsync();

        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {json}");
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, await CountCorrectionEventsAsync(other));
    }

    /// <summary>
    /// Kind B — boundary pin, holds today (the last day is employed). A GlobalAdmin whose end date is
    /// open-ended recalculating their OWN month is not departed: 200. Guards against a guard that refuses
    /// every self-correction.
    /// </summary>
    [Fact]
    public async Task ActiveGlobalAdmin_NoEndDate_OwnMonth_AsToday_200()
    {
        var self = "S147-GAA-" + Guid.NewGuid().ToString("N")[..8];
        await SeedExportedMonthAsync(self, active: true, end: null);

        var rsp = await PostRecalculateAsync(self, self, Guid.NewGuid());
        var json = await rsp.Content.ReadAsStringAsync();

        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {json}");
        Assert.Equal(1, await CountCorrectionEventsAsync(self));
    }

    // ═════════════════════════════════ HTTP ═════════════════════════════════

    private async Task<HttpResponseMessage> PostRecalculateAsync(string actorId, string employeeId, Guid token)
    {
        var client = _fx.Factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/recalculate")
        {
            Content = JsonContent.Create(new
            {
                profile = new EmploymentProfile
                {
                    EmployeeId = employeeId,
                    AgreementCode = AgreementCode,
                    OkVersion = OkVersion,
                    EmploymentCategory = "Standard",
                    PartTimeFraction = 1.0m,
                    OrgId = OrgId,
                },
                periodStart = Mar01,
                periodEnd = Mar31,
                reason = "S147 departed-self pin",
                idempotencyToken = token,
            }),
        };
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = PayrollHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        });
        var bearer = tokenService.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }

    // ═════════════════════════════════ seeding ═════════════════════════════════

    /// <summary>users + profile + agreement rows (the actor IS this employee), the user's lifecycle state set
    /// directly, February APPROVED (March opens on a Sunday, so its opening week holds February's days),
    /// March weekday hours, a February ledger record (so the opening balance derives), and the first export
    /// record for March — the shape the S144 host pins seed for a 200 recalculation.</summary>
    private async Task SeedExportedMonthAsync(string employeeId, bool active, DateOnly? end)
    {
        await using (var conn = _fx.Db.Create())
        {
            await conn.OpenAsync();
            await RegressionSeed.SeedEmployeeAsync(
                conn, employeeId, OrgId, AgreementCode, OkVersion, partTimeFraction: 1.0m);
            await using var upd = new NpgsqlCommand(
                "UPDATE users SET is_active = @a, employment_end_date = @e, updated_at = NOW() WHERE user_id = @id", conn);
            upd.Parameters.AddWithValue("a", active);
            upd.Parameters.AddWithValue("e", (object?)end ?? DBNull.Value);
            upd.Parameters.AddWithValue("id", employeeId);
            Assert.Equal(1, await upd.ExecuteNonQueryAsync());
        }
        await SeedMonthApprovalAsync(employeeId, Feb01, Feb28);
        await SeedMarchWeekdayHoursAsync(employeeId);
        await FlexLedgerSeed.SeedAsync(_fx.ConnectionString, employeeId, Feb01, closing: 0m);
        await PersistFirstExportRecordAsync(employeeId);
    }

    private async Task SeedMonthApprovalAsync(string employeeId, DateOnly start, DateOnly end)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (
                period_id, employee_id, org_id, period_start, period_end, period_type, status,
                submitted_at, submitted_by, approved_by, approved_at, agreement_code, ok_version)
            VALUES (
                gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', 'APPROVED',
                NOW(), @emp, 'ROOT-S147', NOW(), @agreement, @ok)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("agreement", AgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private async Task SeedMarchWeekdayHoursAsync(string employeeId)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        for (var day = new DateOnly(2026, 3, 2); day <= Mar31; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO time_entries_projection
                    (event_id, employee_id, date, hours, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, outbox_id)
                VALUES
                    (gen_random_uuid(), @emp, @date, 7.4, @agreement, @ok, FALSE, NOW(), @outbox)
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", day);
            cmd.Parameters.AddWithValue("agreement", AgreementCode);
            cmd.Parameters.AddWithValue("ok", OkVersion);
            cmd.Parameters.AddWithValue("outbox", Interlocked.Increment(ref _outboxSeq));
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
    }

    private async Task PersistFirstExportRecordAsync(string employeeId)
    {
        var lines = new[]
        {
            new PayrollExportLine
            {
                EmployeeId = employeeId,
                WageType = "SLS_0110",
                Hours = 162.8m,
                Amount = 0m,
                PeriodStart = Mar01,
                PeriodEnd = Mar31,
                OkVersion = OkVersion,
                SourceRuleId = "NORM_CHECK_37H",
                SourceTimeType = "NORMAL_HOURS",
            },
        };
        var ordered = PayrollExportManifest.OrderLines(lines);
        var json = PayrollExportManifest.Serialize(ordered);
        var hash = PayrollExportManifest.ComputeContentHash(ordered);

        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records (
                export_id, period_id, employee_id, year, month, exported_at,
                original_lines, current_effective_lines, content_hash, source
            ) VALUES (
                @id, NULL, @emp, @year, @month, NOW(),
                @lines::jsonb, @lines::jsonb, @hash, 'CALCULATE_AND_EXPORT'
            )
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("year", Year);
        cmd.Parameters.AddWithValue("month", Month);
        cmd.Parameters.Add(new NpgsqlParameter("lines", NpgsqlDbType.Jsonb) { Value = json });
        cmd.Parameters.AddWithValue("hash", hash);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private async Task MarkTokenDeliveredAsync(Guid token)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO outbox_messages (destination, payload, status, idempotency_token, delivered_at)
            VALUES ('payroll', '{}'::jsonb, 'delivered', @t, NOW())
            """, conn);
        cmd.Parameters.AddWithValue("t", token);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ═════════════════════════════════ reads / assertions ═════════════════════════════════

    /// <summary>The March record's effective lines AND its three balance columns, as one string.</summary>
    private async Task<string> ReadLinesAndBalancesAsync(string employeeId)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT current_effective_lines::text, COALESCE(opening_balance::text, ''),
                   COALESCE(closing_balance::text, ''), COALESCE(balance_provenance, '')
            FROM payroll_export_records
            WHERE employee_id = @e AND year = @y AND month = @m
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", Year);
        cmd.Parameters.AddWithValue("m", Month);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "no payroll_export_records row");
        return string.Join("|", reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    private async Task<int> CountCorrectionEventsAsync(string employeeId)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'RetroactiveCorrectionRequested' AND stream_id LIKE @s", conn);
        cmd.Parameters.AddWithValue("s", $"retro-correction-{employeeId}-%");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task AssertNothingWrittenAsync(string employeeId, Guid token, string linesAndBalancesBefore)
    {
        Assert.Equal(0, await CountCorrectionEventsAsync(employeeId));
        Assert.Equal(linesAndBalancesBefore, await ReadLinesAndBalancesAsync(employeeId));

        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using (var audit = new NpgsqlCommand(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'RetroactiveCorrectionRequested' AND target_resource_id = @e", conn))
        {
            audit.Parameters.AddWithValue("e", employeeId);
            Assert.Equal(0, Convert.ToInt32(await audit.ExecuteScalarAsync()));
        }
        await using (var marker = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t", conn))
        {
            marker.Parameters.AddWithValue("t", token);
            Assert.Equal(0, Convert.ToInt32(await marker.ExecuteScalarAsync()));
        }
        await using (var manifests = new NpgsqlCommand(
            "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", conn))
        {
            manifests.Parameters.AddWithValue("e", employeeId);
            Assert.Equal(0, Convert.ToInt32(await manifests.ExecuteScalarAsync()));
        }
    }
}
