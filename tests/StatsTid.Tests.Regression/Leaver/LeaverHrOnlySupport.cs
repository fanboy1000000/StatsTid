using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Payroll;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Xunit;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// S147 / TASK-14713-T — the shared support of the "HR only from day 6" facts (AC-230-2, -6 … -13): the
/// timeline, the seeding, the tokens, the HTTP calls and the exact-body assertions. One file so that every
/// class in <c>tests/StatsTid.Tests.Regression/Leaver/</c> that pins a piece of the owner's OQ-2 timeline
/// reads the same story.
///
/// <para><b>The story (plain words).</b> Mette's last working day is Friday 13 March 2026 (D). Through day 5
/// (18 March) her manager can still approve, reject or reopen her March. On day 6 (19 March) the leaver job
/// has switched her off, and from then only an HR-tier person (and payroll's LocalAdmin) can finish her final
/// month: the manager and anyone holding only a leader scope are refused. HR may act only on a month in which
/// she was actually employed; any other month is refused with an error that carries no date. If HR sent the
/// month, a second HR person must approve it (four-eyes).</para>
///
/// <para><b>Where state comes from.</b> Unless a fact says it lets Step A run, "switched off" is seeded
/// directly (<c>is_active = FALSE</c>) and the Backend host this fixture boots has NO leaver job
/// (<c>withoutLeaverJob: true</c>, the TASK-14719 lesson): otherwise the job, which polls at host start and every five
/// minutes, could flip a grace-state subject mid-fact and the fact would test the wrong state. The one class that needs the
/// real Step A (<see cref="LeaverHrOnlyTests"/>) runs its own container and keeps the job.</para>
///
/// <para>Docker-gated by its users: CI-pending, never locally green.</para>
/// </summary>
internal static class LeaverHrOnly
{
    public const string DevSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>STY02 (agreement HK), seeded by init.sql: the leaver's organisation.</summary>
    public const string HkOrg = "STY02";

    /// <summary>STY01 (agreement AC), a different existing organisation: the "disjoint" scope of a mixed-role token.</summary>
    public const string OtherOrg = "STY01";

    public const string FourEyesReason = "the sender of a month cannot approve or reject it (four-eyes)";
    public const string BelowHrReason = "Terminated-employee access requires LocalHR or above";
    public const string ScopeNotCoveringReason = "Actor scope does not cover target organization";

    /// <summary>Mette's last day employed, D: Friday 2026-03-13 (ADR-040 D1, the end date is inclusive).</summary>
    public static readonly DateOnly LastDay = new(2026, 3, 13);

    /// <summary>D + <paramref name="n"/>. D+2 is Sunday 15 March, D+3 Monday 16, D+5 Wednesday 18, D+6 Thursday 19.</summary>
    public static DateOnly Day(int n) => LastDay.AddDays(n);

    public static (DateOnly Start, DateOnly End) Month(int month)
        => (new DateOnly(2026, month, 1), new DateOnly(2026, month, DateTime.DaysInMonth(2026, month)));

    // A monotonic seq for hand-written absence rows' outbox_id (a NOT-NULL BIGINT), clear of the other suites
    // (the send matrix uses 71_271_000; the allocation gate 900_000; S116 51_160_000).
    private static long _absenceSeq = 74_713_000;
    private static long NextOutboxId() => Interlocked.Increment(ref _absenceSeq);

    public static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid().ToString("N")[..6]}";

    // ══════════════════════════════════════════════════════════════════════
    // Tokens
    // ══════════════════════════════════════════════════════════════════════

    public static string Token(string actorId, string role, string orgClaim, params RoleScope[] scopes)
        => new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid", Audience = "statstid", SigningKey = DevSigningKey, ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: actorId, name: actorId, role: role, agreementCode: "HK", orgId: orgClaim, scopes: scopes);

    public static string GlobalAdminToken(string actorId)
        => Token(actorId, StatsTidRoles.GlobalAdmin, "/", new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL"));

    public static HttpClient WithBearer(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A token whose only scope is <paramref name="role"/> over <paramref name="org"/> (the one the role would naturally carry).</summary>
    public static string RoleToken(string actorId, string role, string org = HkOrg)
        => Token(actorId, role, org, new RoleScope(role, org, "ORG_ONLY"));

    /// <summary>The mixed-role token of the S76 / R9f1 trap: primary role LocalHR, but its HR scope is over a DISJOINT
    /// organisation and the only scope covering <see cref="HkOrg"/> is a LEADER scope.</summary>
    public static string MixedRoleToken(string actorId)
        => Token(actorId, StatsTidRoles.LocalHR, OtherOrg,
            new RoleScope(StatsTidRoles.LocalHR, OtherOrg, "ORG_ONLY"),
            new RoleScope(StatsTidRoles.LocalLeader, HkOrg, "ORG_ONLY"));

    // ══════════════════════════════════════════════════════════════════════
    // Seeding (every statement parameterised; the placeholders are checked by grep in the report)
    // ══════════════════════════════════════════════════════════════════════

    public static Task SeedEmployeeAsync(string cs, string id, string org = HkOrg)
        => RegressionSeed.SeedEmployeeAsync(cs, id, org, "HK", "OK24", ensureOrg: false);

    public static async Task SetWindowAsync(string cs, string id, DateOnly? start, DateOnly? end)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_start_date = @s, employment_end_date = @e, updated_at = NOW() WHERE user_id = @id", conn);
        cmd.Parameters.Add(new NpgsqlParameter("s", NpgsqlDbType.Date) { Value = (object?)start ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("e", NpgsqlDbType.Date) { Value = (object?)end ?? DBNull.Value });
        cmd.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync()); // the user must exist, or the premise is vacuous
    }

    /// <summary>The "Step A has run" state, seeded directly: <c>is_active = FALSE</c>. The row count is asserted.</summary>
    public static async Task DeactivateAsync(string cs, string id)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET is_active = FALSE, updated_at = NOW() WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>A leaver: employed from <paramref name="start"/> (NULL = unbounded) through <paramref name="end"/>
    /// inclusive; switched off when <paramref name="deactivated"/>.</summary>
    public static async Task<string> SeedLeaverAsync(
        string cs, string prefix, DateOnly? end = null, DateOnly? start = null, bool deactivated = true)
    {
        var id = NewId(prefix);
        await SeedEmployeeAsync(cs, id);
        await SetWindowAsync(cs, id, start, end ?? LastDay);
        if (deactivated)
            await DeactivateAsync(cs, id);
        return id;
    }

    /// <summary>A LocalLeader who is the employee's designated PRIMARY manager (a reporting-line edge). The
    /// manager may be switched off, or past their own last day, to pin the edge path's activity test.</summary>
    public static async Task<string> SeedManagerAsync(
        string cs, string prefix, string manages, bool active = true, DateOnly? endDate = null)
    {
        var id = NewId(prefix);
        await SeedEmployeeAsync(cs, id);
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using (var ra = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@u, 'LOCAL_LEADER', @org, 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            ra.Parameters.AddWithValue("u", id);
            ra.Parameters.AddWithValue("org", HkOrg);
            await ra.ExecuteNonQueryAsync();
        }
        await using (var rl = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines
                (employee_id, manager_id, organisation_id, relationship, effective_from, created_by)
            VALUES (@e, @m, @org, 'PRIMARY', @from, 'test_s147_hronly')
            """, conn))
        {
            rl.Parameters.AddWithValue("e", manages);
            rl.Parameters.AddWithValue("m", id);
            rl.Parameters.AddWithValue("org", HkOrg);
            rl.Parameters.AddWithValue("from", new DateOnly(2024, 1, 1));
            await rl.ExecuteNonQueryAsync();
        }
        if (endDate is not null)
            await SetWindowAsync(cs, id, null, endDate);
        if (!active)
            await DeactivateAsync(cs, id);
        return id;
    }

    /// <summary>Seeds one MONTHLY approval row for calendar <paramref name="month"/> of 2026, carrying exactly the stamps named
    /// (any may be NULL). A stored <paramref name="managerDeadline"/> makes the HRP-011 aging anchor observable.</summary>
    public static async Task<Guid> SeedPeriodAsync(
        string cs, string employeeId, string status, int month = 3,
        string? submittedBy = null, string? employeeApprovedBy = null, string? approvedBy = null,
        DateOnly? managerDeadline = null)
    {
        var (start, end) = Month(month);
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version,
                 submitted_at, submitted_by, employee_approved_at, employee_approved_by, approved_at, approved_by,
                 manager_deadline)
            VALUES
                (@e, @org, @s, @en, 'MONTHLY', @st, 'HK', 'OK24',
                 CASE WHEN @sb IS NULL THEN NULL ELSE NOW() END, @sb,
                 CASE WHEN @eab IS NULL THEN NULL ELSE NOW() END, @eab,
                 CASE WHEN @ab IS NULL THEN NULL ELSE NOW() END, @ab,
                 @md)
            RETURNING period_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("org", HkOrg);
        cmd.Parameters.AddWithValue("s", start);
        cmd.Parameters.AddWithValue("en", end);
        cmd.Parameters.AddWithValue("st", status);
        // Typed explicitly: an untyped NULL used in an "IS NULL" test fails 42P08 (the CI-W2 lesson).
        cmd.Parameters.Add(new NpgsqlParameter("sb", NpgsqlDbType.Text) { Value = (object?)submittedBy ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("eab", NpgsqlDbType.Text) { Value = (object?)employeeApprovedBy ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("ab", NpgsqlDbType.Text) { Value = (object?)approvedBy ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("md", NpgsqlDbType.Date) { Value = (object?)managerDeadline ?? DBNull.Value });
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Full-day VACATION absences on every WEEKDAY of [from, to] (a weekday: a zero-norm day is rejected by the
    /// product and skipped by revaluation, S138 / QUAL-153). March 2026 carries no Danish public holiday (asserted).</summary>
    public static async Task CoverWeekdaysAsync(string cs, string employeeId, DateOnly from, DateOnly to)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using (var holidays = new NpgsqlCommand(
            "SELECT COUNT(*) FROM danish_public_holidays WHERE holiday_date >= @s AND holiday_date <= @e", conn))
        {
            holidays.Parameters.AddWithValue("s", from);
            holidays.Parameters.AddWithValue("e", to);
            Assert.Equal(0L, (long)(await holidays.ExecuteScalarAsync())!);
        }
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO absences_projection
                    (event_id, employee_id, date, absence_type, hours, feriedage,
                     agreement_code, ok_version, occurred_at, outbox_id)
                VALUES (gen_random_uuid(), @emp, @date, 'VACATION', 7.4, 1.0, 'HK', 'OK24', NOW(), @seq)
                ON CONFLICT DO NOTHING
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", d);
            cmd.Parameters.AddWithValue("seq", NextOutboxId());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>The two things payroll's chain needs before it will export March for a continuously employed person: February
    /// APPROVED (March's opening week holds 23-27 February) and February recorded in the flex ledger (closing 1.0), so March
    /// opens CHAINED at 1.0.</summary>
    public static async Task SeedPriorMonthAsync(string cs, string employeeId)
    {
        await SeedPeriodAsync(cs, employeeId, "APPROVED", month: 2, approvedBy: "lho_prior_approver");
        await FlexLedgerSeed.SeedAsync(cs, employeeId, new DateOnly(2026, 2, 1), closing: 1.0m, opening: 0m);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Reads (no copy of the rule under test)
    // ══════════════════════════════════════════════════════════════════════

    public sealed record PeriodRow(string Status, string? ApprovedBy, string? EmployeeApprovedBy, string? RejectionReason);

    public static async Task<PeriodRow> ReadPeriodAsync(string cs, Guid periodId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status, approved_by, employee_approved_by, rejection_reason FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), $"period {periodId} must exist");
        return new PeriodRow(
            r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3));
    }

#pragma warning disable CA2100 // every sql passed here is a hardcoded literal at the call site; values flow through parameters.
    public static async Task<long> ScalarAsync(string cs, string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
#pragma warning restore CA2100

    public static Task<long> CountApprovalAuditAsync(string cs, Guid periodId)
        => ScalarAsync(cs, "SELECT COUNT(*) FROM approval_audit WHERE period_id = @id", ("id", periodId));

    /// <summary>Events on the month's approval stream (the approve / reject / reopen / send events). A refused call writes none.</summary>
    public static Task<long> CountApprovalEventsAsync(string cs, string employeeId, int month)
        => ScalarAsync(cs, "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s",
            ("s", $"approval-{employeeId}-{Month(month).Start:yyyy-MM-dd}"));

    /// <summary>The month's natural key lookup (employee, the whole calendar month): the row a send wrote.</summary>
    public static async Task<Guid> FindPeriodIdAsync(string cs, string employeeId, int month)
    {
        var (start, end) = Month(month);
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT period_id FROM approval_periods WHERE employee_id = @e AND period_start = @s AND period_end = @en", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("s", start);
        cmd.Parameters.AddWithValue("en", end);
        var id = await cmd.ExecuteScalarAsync();
        Assert.True(id is Guid, $"the send must have written a month row for {employeeId}");
        return (Guid)id!;
    }

    /// <summary>A send that worked: 200 and the month now EMPLOYEE_APPROVED (HR certifying on the employee's behalf).</summary>
    public static async Task AssertSendOkAsync(HttpResponseMessage rsp, string context)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"{context}: expected 200, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal("EMPLOYEE_APPROVED", doc.RootElement.GetProperty("status").GetString());
    }

    /// <summary>The four-eyes refusal: 403 and exactly the date-free reason of TASK-14704's contract.</summary>
    public static async Task AssertFourEyesRefusalAsync(HttpResponseMessage rsp, string context)
    {
        var (_, reason) = await AssertForbiddenAsync(rsp, context);
        Assert.Equal(FourEyesReason, reason);
    }

    public sealed record ExportRecord(decimal? Opening, decimal? Closing, string? Provenance);

    public static async Task<ExportRecord?> ReadExportRecordAsync(string cs, string employeeId, int month)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT opening_balance, closing_balance, balance_provenance
            FROM payroll_export_records WHERE employee_id = @e AND year = @y AND month = @m
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", 2026);
        cmd.Parameters.AddWithValue("m", month);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return null;
        return new ExportRecord(
            r.IsDBNull(0) ? null : r.GetDecimal(0),
            r.IsDBNull(1) ? null : r.GetDecimal(1),
            r.IsDBNull(2) ? null : r.GetString(2));
    }

    // ══════════════════════════════════════════════════════════════════════
    // Backend HTTP
    // ══════════════════════════════════════════════════════════════════════

    public static Task<HttpResponseMessage> ApproveAsync(HttpClient c, Guid periodId)
        => c.PostAsync($"/api/approval/{periodId}/approve", content: null);

    public static Task<HttpResponseMessage> RejectAsync(HttpClient c, Guid periodId)
        => c.PostAsJsonAsync($"/api/approval/{periodId}/reject", new { reason = "s147 hr-only pin" });

    public static Task<HttpResponseMessage> ReopenAsync(HttpClient c, Guid periodId)
        => c.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "s147 hr-only pin" });

    public static Task<HttpResponseMessage> SendAsync(HttpClient c, string employeeId, int month = 3)
        => c.PostAsJsonAsync("/api/approval/send", new { employeeId, year = 2026, month });

    public static Task<HttpResponseMessage> SkemaMonthAsync(HttpClient c, string employeeId, int month)
        => c.GetAsync($"/api/skema/{employeeId}/month?year=2026&month={month}");

    /// <summary>The three decision handlers and the Skema month view behind one switch, so a fact can be run per handler.</summary>
    public static Task<HttpResponseMessage> ActAsync(string handler, HttpClient c, string employeeId, Guid periodId, int month)
        => handler switch
        {
            "approve" => ApproveAsync(c, periodId),
            "reject" => RejectAsync(c, periodId),
            "reopen" => ReopenAsync(c, periodId),
            "skema" => SkemaMonthAsync(c, employeeId, month),
            _ => throw new ArgumentOutOfRangeException(nameof(handler), handler, null),
        };

    /// <summary>The status a successful decision leaves on the month (the Skema view changes nothing).</summary>
    public static string StatusAfter(string handler) => handler switch
    {
        "approve" => "APPROVED",
        "reject" => "REJECTED",
        "reopen" => "DRAFT",
        _ => "EMPLOYEE_APPROVED",
    };

    // ══════════════════════════════════════════════════════════════════════
    // Exact-body assertions
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-040 D3's date-free refusal, EXACTLY: 422 and a body of precisely <c>error</c>, <c>kind</c>, <c>message</c> (plus
    /// <c>success:false</c> on payroll's copy) with the Orchestrator-ruled values, and not one digit anywhere in it, so no date can
    /// have leaked. Returns the raw body, so a caller can compare two refusals byte for byte (before-start and after-end
    /// must be indistinguishable).
    /// </summary>
    public static async Task<string> AssertDateFreeRefusalAsync(HttpResponseMessage rsp, bool payroll = false)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity, $"expected 422, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var names = root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var expected = payroll
            ? new[] { "error", "kind", "message", "success" }
            : new[] { "error", "kind", "message" };
        Assert.Equal(expected, names);
        Assert.Equal("month_outside_employment_period", root.GetProperty("error").GetString());
        Assert.Equal("employment-window", root.GetProperty("kind").GetString());
        Assert.Equal("Måneden ligger uden for ansættelsesperioden.", root.GetProperty("message").GetString());
        if (payroll)
            Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(Regex.IsMatch(raw, @"\d"), $"the refusal must carry no date (no digit at all): {raw}");
        return raw;
    }

    /// <summary>A 403, and the body's <c>reason</c> when the refusal is a handler's own (a policy refusal has no body).</summary>
    public static async Task<(string Raw, string? Reason)> AssertForbiddenAsync(HttpResponseMessage rsp, string context)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"{context}: expected 403, got {(int)rsp.StatusCode}: {raw}");
        string? reason = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("reason", out var r))
                reason = r.GetString();
        }
        return (raw, reason);
    }

    public static async Task AssertStatusAsync(HttpResponseMessage rsp, HttpStatusCode expected, string context)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == expected, $"{context}: expected {(int)expected}, got {(int)rsp.StatusCode}: {raw}");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Payroll host (the REAL Payroll host over the same database, a configured Rule Engine stand-in behind it)
    // ══════════════════════════════════════════════════════════════════════

    public static (PayrollHostFactory Base, WebApplicationFactory<RetroactiveCorrectionService> Host) BuildPayrollHost(
        string cs, FlexLedgerStubEngine engine)
    {
        var baseFactory = new PayrollHostFactory(cs, new RuleRegistry().GetAll());
        var host = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new StubClientFactory(engine));
        }));
        return (baseFactory, host);
    }

    private sealed class StubClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubClientFactory(FlexLedgerStubEngine engine) => _handler = new TestFixtures.StubHandler(engine.Handle);
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    public static EmploymentProfile Profile(string employeeId) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = "HK",
        OkVersion = "OK24",
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = HkOrg,
    };

    public static async Task<(HttpStatusCode Status, string Body)> CalculateAndExportAsync(
        WebApplicationFactory<RetroactiveCorrectionService> host, string token, string employeeId, int month)
    {
        var (start, end) = Month(month);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/calculate-and-export")
        {
            Content = JsonContent.Create(new
            {
                profile = Profile(employeeId),
                entries = Array.Empty<TimeEntry>(),
                absences = Array.Empty<AbsenceEntry>(),
                periodStart = start,
                periodEnd = end,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var rsp = await host.CreateClient().SendAsync(request);
        return (rsp.StatusCode, await rsp.Content.ReadAsStringAsync());
    }

    public static async Task<(HttpStatusCode Status, string Body)> RecalculateAsync(
        WebApplicationFactory<RetroactiveCorrectionService> host, string token, string employeeId, int month)
    {
        var (start, end) = Month(month);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/recalculate")
        {
            Content = JsonContent.Create(new
            {
                profile = Profile(employeeId),
                entries = Array.Empty<TimeEntry>(),
                absences = Array.Empty<AbsenceEntry>(),
                periodStart = start,
                periodEnd = end,
                reason = "S147 AC-230-13 characterization",
                idempotencyToken = Guid.NewGuid(),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var rsp = await host.CreateClient().SendAsync(request);
        return (rsp.StatusCode, await rsp.Content.ReadAsStringAsync());
    }

    public static string LocalAdminToken(string actorId, string org = HkOrg)
        => RoleToken(actorId, StatsTidRoles.LocalAdmin, org);

    /// <summary>The payroll side of "HR only from day 6": the one Rule Engine stand-in's counters answer "nothing was calculated".</summary>
    public static int EngineCalls(FlexLedgerStubEngine engine, string employeeId) => engine.CallCountFor(employeeId);
}

/// <summary>
/// One Postgres container (the real <c>init.sql</c>) + the Backend host WITHOUT the leaver job + the Payroll host over the same
/// database, shared by every "HR only from day 6" class that joins the <c>LeaverHrOnly</c> collection. Isolation between facts
/// is by CASE-UNIQUE identity (each fact owns its own employees), never by cleanup.
/// </summary>
public sealed class LeaverHrOnlyFixture : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private PayrollHostFactory _payrollBase = null!;
    private readonly Dictionary<DateOnly, WebApplicationFactory<Program>> _hosts = new();

    public string ConnectionString { get; private set; } = null!;
    public StatsTidWebApplicationFactory Factory { get; private set; } = null!;
    internal FlexLedgerStubEngine Engine { get; } = new();
    public WebApplicationFactory<RetroactiveCorrectionService> PayrollHost { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        ConnectionString = _harness.ConnectionString;
        // No leaver job: seeded grace-state and switched-off subjects stay exactly as a fact seeded them.
        Factory = new StatsTidWebApplicationFactory(ConnectionString, withoutLeaverJob: true);
        await TestFixtures.SeedWageTypeMappingsAsync(new DbConnectionFactory(ConnectionString));
        var (baseFactory, host) = LeaverHrOnly.BuildPayrollHost(ConnectionString, Engine);
        _payrollBase = baseFactory;
        PayrollHost = host;
    }

    public async Task DisposeAsync()
    {
        foreach (var h in _hosts.Values) h.Dispose();
        if (PayrollHost is not null)
            await PayrollHost.DisposeAsync();
        if (_payrollBase is not null)
            await _payrollBase.DisposeAsync();
        Factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>A Backend host whose Copenhagen "today" is <paramref name="today"/> (booted once per date, before any fixture
    /// for a fact is seeded against it: a derived host's first client re-runs the startup seeders).</summary>
    public WebApplicationFactory<Program> HostAt(DateOnly today)
    {
        if (!_hosts.TryGetValue(today, out var host))
        {
            host = Factory.WithFixedToday(today);
            _ = host.CreateClient();
            _hosts[today] = host;
        }
        return host;
    }
}

[CollectionDefinition("LeaverHrOnly")]
public sealed class LeaverHrOnlyCollection : ICollectionFixture<LeaverHrOnlyFixture> { }

/// <summary>The base of the "LeaverHrOnly" collection classes: thin wrappers that bind the static helpers to the fixture.</summary>
public abstract class LeaverHrOnlyTestBase
{
    protected readonly LeaverHrOnlyFixture Fx;

    protected LeaverHrOnlyTestBase(LeaverHrOnlyFixture fx) => Fx = fx;

    protected string Cs => Fx.ConnectionString;

    /// <summary>A client on the Backend host whose clock is pinned to <paramref name="day"/>.</summary>
    protected HttpClient ClientAt(DateOnly day, string token)
        => LeaverHrOnly.WithBearer(Fx.HostAt(day).CreateClient(), token);

    protected HttpClient HrAt(DateOnly day, string actorId)
        => ClientAt(day, LeaverHrOnly.RoleToken(actorId, StatsTidRoles.LocalHR));

    protected HttpClient LeaderAt(DateOnly day, string actorId)
        => ClientAt(day, LeaverHrOnly.RoleToken(actorId, StatsTidRoles.LocalLeader));

    /// <summary>A client on the Payroll host.</summary>
    protected Task<(HttpStatusCode Status, string Body)> ExportAsync(string token, string employeeId, int month = 3)
        => LeaverHrOnly.CalculateAndExportAsync(Fx.PayrollHost, token, employeeId, month);

    /// <summary>Everything payroll needs to export a leaver's March: February approved and recorded, and a configured closing for
    /// March from the Rule Engine stand-in.</summary>
    protected async Task PrepareExportableMarchAsync(string employeeId, decimal marchClosing)
    {
        await LeaverHrOnly.SeedPriorMonthAsync(Cs, employeeId);
        Fx.Engine.Configure(employeeId, new DateOnly(2026, 3, 1), closing: marchClosing);
    }
}
