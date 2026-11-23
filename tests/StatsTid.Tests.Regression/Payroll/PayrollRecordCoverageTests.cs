using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using StatsTid.Tests.Regression.Concurrency;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Concurrency.PayInputsLockTestKit;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14847-T (AC-PIL-i (6)-(9); QUAL-265) - payroll refuses a month in which the employment record stops covering
/// a day of a piece it pays, and leaves unbroken and replaced timelines alone, through the real routes and a real database.
///
/// <para>
/// <b>In plain language.</b> When HR closes an employee's profile partway through a month while the person stays employed, the
/// days after the close have no profile. Payroll used to look at only the first day of each stretch of days, find the profile
/// still there, and pay the remaining days on the ended profile without telling anyone. It now looks at each stretch's last day
/// too, and a month whose record ends inside a stretch is refused by name: 422 <c>payroll-employment-record-gap</c>, "an HR
/// administrator must repair the employment record", with nothing calculated or written (the correction of an exported month
/// answers the same, and the recorded month stays as it was). A month whose record is replaced (a new row from a date) or whose
/// timeline merely holds a retired zero-length row must still be paid.
/// </para>
///
/// <para>
/// <b>Fixture.</b> One database per fact, a Backend host whose clock is Tuesday 31 March 2026 and a payroll host
/// (<see cref="PayInputsLockTestKit"/>); one HK / OK24 employee in STY01 with February and March 2026 APPROVED and weekday hours
/// recorded. The 422 body is asserted by <see cref="PayrollEmploymentRecordGapTests.AssertRecordGapAsync"/>: exactly the three
/// members <c>success</c>, <c>error</c>, <c>kind</c>, the ruled sentence, no date and no employee id (ADR-040 D7).
/// Docker-gated: <b>CI-pending</b>. (6) and (7) are F (RED at <c>f493bb32</c>, which pays the month); (8) and (9) are R (green
/// before and after).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollRecordCoverageTests : DatedConfigDockerFixture, IDisposable
{
    private const string Org = "STY01";

    private static readonly DateOnly CloseClock = new(2026, 3, 31);   // a Tuesday
    private static readonly DateOnly Feb01 = new(2026, 2, 1);
    private static readonly DateOnly Feb28 = new(2026, 2, 28);
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar16 = new(2026, 3, 16);        // a Monday
    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    private readonly List<WebApplicationFactory<Program>> _hosts = new();
    private readonly List<PayrollHost> _payrolls = new();

    public void Dispose()
    {
        foreach (var payroll in _payrolls)
            payroll.DisposeAsync().AsTask().GetAwaiter().GetResult();
        foreach (var host in _hosts)
            host.Dispose();
    }

    private sealed record Scene(HttpClient Admin, PayrollHost Payroll, string Employee);

    /// <summary>A booted Backend host (a GlobalAdmin client), a payroll host and the employee, as in the race facts.</summary>
    private async Task<Scene> SceneAsync()
    {
        var host = Factory.WithFixedToday(CloseClock);
        _hosts.Add(host);
        var admin = GlobalAdminClient(host); // boots the host before anything is seeded (PAT-008)
        await TestFixtures.SeedWageTypeMappingsAsync(Db);
        var payroll = PayrollHost.Start(ConnectionString);
        _payrolls.Add(payroll);
        var employee = await SeedPayableEmployeeAsync(ConnectionString, Org, (Feb01, Feb28), (Mar01, Mar31));
        return new Scene(admin, payroll, employee);
    }

    private Task<long> VersionAsync(string employee) =>
        ScalarAsync<long>("SELECT version FROM users WHERE user_id = @u", ("u", employee));

    /// <summary>The profile close, as the HR route does it: <c>DELETE /api/admin/employee-profiles/{id}</c> with the user's
    /// version in <c>If-Match</c>. It ends the profile on the clock's day while the person stays employed. Answers 204.</summary>
    private async Task CloseProfileAsync(Scene s)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{s.Employee}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{await VersionAsync(s.Employee)}\"");
        using var close = await s.Admin.SendAsync(request);
        Assert.True(close.StatusCode == HttpStatusCode.NoContent, $"the close: {(int)close.StatusCode} {await close.Content.ReadAsStringAsync()}");
    }

    private Task<long> ExportRecordCountAsync(string employee) => CountAsync(
        "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e AND year = 2026 AND month = 3", ("e", employee));

    /// <summary>The month's record as one comparable string: hash, lines and balances.</summary>
    private Task<string?> RecordStateAsync(string employee) => ScalarAsync<string>(
        """
        SELECT content_hash || '|' || current_effective_lines::text || '|' || original_lines::text || '|' ||
               COALESCE(opening_balance::text, '-') || '|' || COALESCE(closing_balance::text, '-')
        FROM payroll_export_records WHERE employee_id = @e AND year = 2026 AND month = 3
        """, ("e", employee));

    /// <summary><b>F (CI).</b> AC-PIL-i (6). The clock reads Tuesday 31 March 2026 and the employee's March is APPROVED. HR closes
    /// the profile through the route (204: the profile now ends on the 31st while the person stays employed). The calculation and
    /// export of March answers 422 <c>payroll-employment-record-gap</c>, three members byte for byte, and nothing is written: no
    /// export record, no <c>PayrollExportGenerated</c>, no audit row, no delivery envelope, no manifest (the refusal comes before
    /// the first rule call), no worklist row.
    /// <para><b>Red conditions.</b> At <c>f493bb32</c> the month is paid: the route answers 200 with the days of the 31st on the
    /// ended profile, so the status assertion in <c>AssertRecordGapAsync</c> fails.</para></summary>
    [Fact]
    public async Task ProfileClosedInsideTheMonth_CalculateAndExport422Named_NothingWritten()
    {
        var s = await SceneAsync();
        await CloseProfileAsync(s);

        using var response = await s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31);

        await PayrollEmploymentRecordGapTests.AssertRecordGapAsync(response, s.Employee);
        Assert.Equal(0L, await ExportRecordCountAsync(s.Employee));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'PayrollExportGenerated'", ("s", $"employee-{s.Employee}")));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'PayrollExportGenerated' AND target_resource_id = @e", ("e", s.Employee)));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_messages WHERE destination = 'payroll' AND payload::text LIKE '%' || @e || '%'", ("e", s.Employee)));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", s.Employee)));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @e", ("e", s.Employee)));
    }

    /// <summary><b>F (CI).</b> AC-PIL-i (7). March is exported through the route first (200), then HR closes the profile (204, which
    /// lists March on the worklist). The correction (<c>/recalculate</c>) of March answers the same 422, the exported record is
    /// unchanged (hash, lines and balances), the worklist row for March is still open, and no correction event, audit row or
    /// idempotency marker was written for the request's token.
    /// <para><b>Red conditions.</b> At <c>f493bb32</c> the correction recalculates the month on the ended profile and answers 200,
    /// so the status assertion fails.</para></summary>
    [Fact]
    public async Task ProfileClosedInsideAnExportedMonth_Recalculate422Named_RecordUnchanged_RowOpen()
    {
        var s = await SceneAsync();
        using (var first = await s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31))
            Assert.True(first.StatusCode == HttpStatusCode.OK, $"the first export: {await first.Content.ReadAsStringAsync()}");
        var recordBefore = await RecordStateAsync(s.Employee);
        Assert.NotNull(recordBefore);
        await CloseProfileAsync(s);
        var manifestsBefore = await CountAsync("SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", s.Employee));
        var token = Guid.NewGuid();

        using var response = await s.Payroll.RecalculateAsync(s.Employee, Org, Mar01, Mar31, token);

        await PayrollEmploymentRecordGapTests.AssertRecordGapAsync(response, s.Employee);
        Assert.Equal(recordBefore, await RecordStateAsync(s.Employee));
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @e AND kind = 'EXPORTED_MONTH' " +
            "AND year = 2026 AND month = 3 AND resolved_at IS NULL", ("e", s.Employee)));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'RetroactiveCorrectionRequested'"));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'RetroactiveCorrectionRequested'"));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t", ("t", token)));
        Assert.Equal(manifestsBefore, await CountAsync("SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", s.Employee)));
    }

    /// <summary><b>R (CI).</b> AC-PIL-i (8). HR replaces the profile from Monday 16 March 2026 through the route (a PUT dated 16 March
    /// that changes the part-time fraction; 200): the old row ends on the 16th and a new row starts there, so the record is
    /// unbroken. The export of March answers 200 and records the month, with a piece starting on the 16th (the new row's start
    /// is a cut point). No false record gap.
    /// <para><b>Red conditions (kind R).</b> A last-day check that probed the old row's end, or refused a replaced row, answers 422
    /// here and fails the status assertion. Green before and after TASK-14847, by design.</para></summary>
    [Fact]
    public async Task ProfileReplacedInsideTheMonth_NoFalseGap()
    {
        var s = await SceneAsync();
        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{s.Employee}")
        {
            Content = JsonContent.Create(new
            {
                effectiveFrom = "2026-03-16",
                partTimeFraction = 0.8m,
                position = (string?)null,
                employmentCategory = (string?)null,
            }),
        };
        put.Headers.TryAddWithoutValidation("If-Match", $"\"{await VersionAsync(s.Employee)}\"");
        using (var replaced = await s.Admin.SendAsync(put))
            Assert.True(replaced.StatusCode == HttpStatusCode.OK, $"the profile PUT: {(int)replaced.StatusCode} {await replaced.Content.ReadAsStringAsync()}");

        using var response = await s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31);

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"the export: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(1L, await ExportRecordCountAsync(s.Employee));
        Assert.Contains(s.Payroll.Engine.LatestTimelinePieces(), p => p.From == Mar16);
    }

    /// <summary><b>R (CI).</b> AC-PIL-i (9). The employee's profile covers all of March, and the timeline also holds a retired
    /// zero-length row dated Monday 16 March 2026 (<c>effective_from = effective_to</c>, the shape the close route gives a scheduled
    /// row it retires). <b>No route yields that state inside a covered month</b>: the close retires only rows scheduled AFTER the
    /// day it ends the profile on, so the row is seeded by SQL. The row is a cut point (its start is read) but covers no day. The
    /// export answers 200 and records the month, with a piece starting on the 16th and no record gap.
    /// <para><b>Red conditions (kind R).</b> A last-day check that counted the zero-length row as a record, or refused the piece it
    /// cuts, answers 422 and fails the status assertion. Green before and after TASK-14847, by design.</para></summary>
    [Fact]
    public async Task RetiredZeroLengthRowInsideACoveredMonth_NoFalseGap()
    {
        var s = await SceneAsync();
        await ExecAsync(
            """
            INSERT INTO employee_profiles
                (profile_id, employee_id, part_time_fraction, position, effective_from, effective_to, version, employment_category)
            VALUES (gen_random_uuid(), @e, 1.000, NULL, DATE '2026-03-16', DATE '2026-03-16', 1,
                    (SELECT u.employment_category FROM users u WHERE u.user_id = @e))
            """, ("e", s.Employee));

        using var response = await s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31);

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"the export: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(1L, await ExportRecordCountAsync(s.Employee));
        Assert.Contains(s.Payroll.Engine.LatestTimelinePieces(), p => p.From == Mar16);
    }
}
