using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S148 TASK-14809-T / AC-S0-5b — the month's send check compares CLOCK-HOUR totals with project hours
/// and nothing else. "Manual hours" (a bare daily total with no clock times) are retired; the send
/// check's worked side is Σ interval hours alone (ADR-028 D4 as amended by S148).
///
/// <para>Kind <b>F (CI)</b>. The one way a fixture can still put manual hours into the product is a
/// legacy client posting a stray <c>manualHours</c> member in a Skema work-time day. This fact does that
/// DELIBERATELY: the day is one interval 08:00–12:00 (4,0 h) plus <c>manualHours: 3.4</c>, saved over
/// HTTP through the real save endpoint. <b>The fact pins that a stray <c>manualHours</c> member is
/// ignored; it does not make accepting the member a contract.</b></para>
///
/// <list type="bullet">
///   <item><b>(i)</b> project hours 4,0 on Thursday 5 March 2026 → the send answers 200 and the period
///     is <c>EMPLOYEE_APPROVED</c>: interval hours alone (4,0) balance the project hours.</item>
///   <item><b>(ii)</b> project hours 7,4 → 422 <c>kind:"allocation"</c> with exactly one unbalanced day,
///     2026-03-05, worked 4.00, allocated 7.40, direction <c>"over"</c> (<c>AllocationBalance.Direction</c>:
///     "under" when worked &gt; allocated, "over" otherwise): hours a client sent as manual are not
///     counted, so the send check's equality catches them as lost hours.</item>
/// </list>
///
/// <para><b>Red conditions</b> (argued against the code before TASK-14809; Docker, so CI runs it): there
/// the stray member is bound into the save, stored in <c>work_time_projection.manual_hours</c> and added
/// to the day's interval hours by the send check, so worked is 7.40. (i) answers 422 (worked 7.40 against
/// 4.00, "under") and (ii) answers 200 (7.40 = 7.40). Each answer fails the assertion it is paired with.
/// The save's own 200 is asserted first, so a failing fixture never passes as a red pin.</para>
///
/// <para>Harness: the production <see cref="AllocationGateFixture"/> (the real <c>init.sql</c> and the
/// real endpoints); March 2026 carries no Danish public holiday (asserted), so covering every other
/// weekday with a full-day VACATION absence leaves the case day as the only day the check compares.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SendCheckIntervalOnlyTests : IClassFixture<AllocationGateFixture>
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string Org = "STY02";

    private static readonly DateOnly MonthStart = new(2026, 3, 1);
    private static readonly DateOnly MonthEnd = new(2026, 3, 31);

    /// <summary>2026-03-05, a Thursday.</summary>
    private static readonly DateOnly CaseDay = new(2026, 3, 5);

    private static long _absenceSeq = 950_000;

    private readonly AllocationGateFixture _fx;

    public SendCheckIntervalOnlyTests(AllocationGateFixture fx) => _fx = fx;

    [Fact]
    public async Task SendCheck_ComparesIntervalHoursWithProjectHours()
    {
        // (i) 4,0 h of clock time + a stray 3,4 h manual member; 4,0 h on a project → sent.
        var balanced = await SeedMonthAsync("s5b_bal");
        await SaveWorkTimeWithStrayManualHoursAsync(balanced);
        await AllocatedAsync(balanced, 4.0m);
        var sent = await SendAsync(balanced);
        var sentRaw = await sent.Content.ReadAsStringAsync();
        Assert.True(sent.StatusCode == HttpStatusCode.OK, $"(i) expected 200, got {(int)sent.StatusCode}: {sentRaw}");
        Assert.Equal("EMPLOYEE_APPROVED", JsonDocument.Parse(sentRaw).RootElement.GetProperty("status").GetString());

        // (ii) the same save; 7,4 h on a project → refused: the 3,4 h the client called manual are not worked.
        var lost = await SeedMonthAsync("s5b_lost");
        await SaveWorkTimeWithStrayManualHoursAsync(lost);
        await AllocatedAsync(lost, 7.4m);
        var refused = await SendAsync(lost);
        var refusedRaw = await refused.Content.ReadAsStringAsync();
        Assert.True(refused.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"(ii) expected 422, got {(int)refused.StatusCode}: {refusedRaw}");
        var body = JsonDocument.Parse(refusedRaw).RootElement;
        Assert.Equal("allocation", body.GetProperty("kind").GetString());
        var day = Assert.Single(body.GetProperty("unbalancedDays").EnumerateArray().ToList());
        Assert.Equal("2026-03-05", day.GetProperty("date").GetString());
        Assert.Equal(4.00m, day.GetProperty("worked").GetDecimal());
        Assert.Equal(7.40m, day.GetProperty("allocated").GetDecimal());
        Assert.Equal("over", day.GetProperty("direction").GetString());
    }

    // ── Helpers (copied in shape from AllocationGateTests / SendConcurrencyTestBase) ──

    /// <summary>The real Skema save over HTTP: one interval 08:00–12:00 and a stray legacy
    /// <c>manualHours</c> member (anonymous-object member, deliberately not a typed field).</summary>
    private async Task SaveWorkTimeWithStrayManualHoursAsync(string employeeId)
    {
        var rsp = await ClientFor(employeeId).PostAsJsonAsync($"/api/skema/{employeeId}/save", new
        {
            year = CaseDay.Year,
            month = CaseDay.Month,
            workTime = new[]
            {
                new
                {
                    date = CaseDay,
                    intervals = new[] { new { start = "08:00", end = "12:00" } },
                    manualHours = 3.4m, // the stray legacy member this fact is about
                },
            },
        });
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"save expected 200, got {(int)rsp.StatusCode}: {raw}");
    }

    private async Task<HttpResponseMessage> SendAsync(string employeeId)
        => await ClientFor(employeeId).PostAsJsonAsync("/api/approval/send", new
        {
            employeeId,
            year = MonthStart.Year,
            month = MonthStart.Month,
        });

    /// <summary>A NORMAL, project-tagged time entry on the case day through the production write path.</summary>
    private async Task AllocatedAsync(string employeeId, decimal hours)
    {
        var @event = new TimeEntryRegistered
        {
            EmployeeId = employeeId,
            Date = CaseDay,
            Hours = hours,
            TaskId = "PROJ-1",
            ActivityType = "NORMAL",
            AgreementCode = "HK",
            OkVersion = "OK24",
        };
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var oid = await _fx.Outbox.EnqueueAndReturnIdAsync(conn, tx, $"employee-{employeeId}", @event);
        await _fx.TimeEntryRepo.InsertAsync(conn, tx, @event, oid);
        await tx.CommitAsync();
    }

    /// <summary>A fresh HK/OK24 employee in STY02 whose every March 2026 weekday EXCEPT the case day is
    /// covered by a full-day VACATION absence (so the coverage check passes and the case day is the
    /// only day compared).</summary>
    private async Task<string> SeedMonthAsync(string caseId)
    {
        var emp = "t14809_" + caseId;
        await RegressionSeed.SeedEmployeeAsync(_fx.ConnectionString, emp, Org, "HK", "OK24", ensureOrg: false);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM danish_public_holidays WHERE holiday_date >= @s AND holiday_date <= @e", conn))
        {
            cmd.Parameters.AddWithValue("s", MonthStart);
            cmd.Parameters.AddWithValue("e", MonthEnd);
            Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
        }

        for (var d = MonthStart; d <= MonthEnd; d = d.AddDays(1))
        {
            if (d == CaseDay || d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO absences_projection
                    (event_id, employee_id, date, absence_type, hours, feriedage,
                     agreement_code, ok_version, occurred_at, outbox_id)
                VALUES (gen_random_uuid(), @emp, @date, 'VACATION', 7.4, 1.0, 'HK', 'OK24', NOW(), @seq)
                """, conn);
            cmd.Parameters.AddWithValue("emp", emp);
            cmd.Parameters.AddWithValue("date", d);
            cmd.Parameters.AddWithValue("seq", Interlocked.Increment(ref _absenceSeq));
            await cmd.ExecuteNonQueryAsync();
        }

        return emp;
    }

    private HttpClient ClientFor(string userId)
    {
        var client = _fx.Factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.Employee,
            agreementCode: "HK", orgId: Org,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, Org, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
