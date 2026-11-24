using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Tests.Regression.Concurrency;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.Payroll;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using static StatsTid.Tests.Regression.Concurrency.PayInputsLockTestKit;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// S148 / TASK-14849-T (QUAL-267, QUAL-265) - the start-up seeder (<see cref="EmployeeProfileSeeder.SeedAsync"/>) gives the year-1
/// default profile to an active person with NO profile row at all, and leaves alone a person whose profile HR closed.
///
/// <para>
/// <b>In plain language.</b> Every time the backend starts it fills in a default profile (full time, valid "since year 1") for
/// each active person who has none. It used to treat "no open profile" as "none", so a person created through the admin screen
/// whose profile HR then closed (the person stays employed) was given an invented full-time profile at the next restart, and
/// payroll paid the days after the close on it instead of refusing the month. The seeder now looks for "no profile row at
/// all": a closed profile is a record gap HR repairs, never a default.
/// </para>
///
/// <para>
/// <b>Fixture.</b> One database per fact; Backend hosts whose clock is pinned (the person is created on one host and closed on
/// a later one, so the profile is a real dated row, not the year-1 anchor). Each fact calls the seeder itself, with the host's
/// own repository and outbox, between its two readings. Docker-gated: <b>CI-pending</b>. (1) and (3) are F (RED at
/// <c>6790ee63</c>, which writes the year-1 row); (2) is R (green before and after, the guard against over-correction).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmployeeProfileSeederClosedProfileTests : DatedConfigDockerFixture, IDisposable
{
    private const string Org = "STY01";

    private static readonly DateOnly Created = new(2026, 2, 2);       // a Monday: the admin create's profile starts here
    private static readonly DateOnly CloseClock = new(2026, 3, 31);   // a Tuesday: the close ends the profile here
    private static readonly DateOnly Feb01 = new(2026, 2, 1);
    private static readonly DateOnly Feb28 = new(2026, 2, 28);
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
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

    /// <summary>A booted host pinned to <paramref name="today"/> and a GlobalAdmin client on it (boots the host, so its start-up
    /// seeders have run before the fact seeds anything).</summary>
    private (WebApplicationFactory<Program> Host, HttpClient Admin) HostAt(DateOnly today)
    {
        var host = Factory.WithFixedToday(today);
        _hosts.Add(host);
        return (host, GlobalAdminClient(host));
    }

    /// <summary>A person made the way the admin screen makes one: <c>POST /api/admin/users</c> (user row, profile row dated the
    /// host's clock, agreement-code row, organisation row, with their events).</summary>
    private static async Task<string> CreateUserAsync(HttpClient admin, string agreementCode)
    {
        var userId = "usr_s148_seed_" + Guid.NewGuid().ToString("N")[..8];
        using var rsp = await admin.PostAsJsonAsync("/api/admin/users", new
        {
            userId,
            username = userId,
            password = "TestPassword123!",
            displayName = "S148 Seeder Test User",
            email = (string?)null,
            primaryOrgId = Org,
            agreementCode,
            okVersion = "OK24",
        });
        Assert.True(rsp.StatusCode == HttpStatusCode.Created, $"the user create: {(int)rsp.StatusCode} {await rsp.Content.ReadAsStringAsync()}");
        return userId;
    }

    /// <summary>The profile close, as the HR route does it: <c>DELETE /api/admin/employee-profiles/{id}</c> with the user's
    /// version in <c>If-Match</c>. It ends the profile on the host's clock day; the person stays active. Answers 204.</summary>
    private async Task CloseProfileAsync(HttpClient admin, string employee)
    {
        var version = await ScalarAsync<long>("SELECT version FROM users WHERE user_id = @u", ("u", employee));
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{employee}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        using var close = await admin.SendAsync(request);
        Assert.True(close.StatusCode == HttpStatusCode.NoContent, $"the close: {(int)close.StatusCode} {await close.Content.ReadAsStringAsync()}");
    }

    /// <summary>The product's seeder, run on the database with the host's own repository and outbox.</summary>
    private Task RunSeederAsync(WebApplicationFactory<Program> host) => EmployeeProfileSeeder.SeedAsync(
        Db, host.Services.GetRequiredService<IOutboxEnqueue>(), host.Services.GetRequiredService<EmployeeProfileRepository>(),
        NullLogger.Instance);

    /// <summary>The person's profile rows as one comparable string: id, dates (as text, so year 1 survives), version, fraction, position.</summary>
    private Task<string?> ProfileRowsAsync(string employee) => ScalarAsync<string>(
        """
        SELECT COALESCE(string_agg(profile_id::text || '|' || effective_from::text || '|' || COALESCE(effective_to::text, 'open') || '|' ||
                                   version::text || '|' || part_time_fraction::text || '|' || COALESCE(position, '-'),
                                   ';' ORDER BY effective_from, profile_id), '')
        FROM employee_profiles WHERE employee_id = @e
        """, ("e", employee));

    private Task<long> ProfileEventCountAsync(string employee, string? actor = null) => CountAsync(
        "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'EmployeeProfileCreated' " +
        "AND (@a::text IS NULL OR actor_id = @a)", ("s", $"employee-profile-{employee}"), ("a", (object?)actor ?? DBNull.Value));

    private Task<long> ProfileAuditCountAsync(string employee) => CountAsync(
        "SELECT COUNT(*) FROM employee_profile_audit WHERE employee_id = @e", ("e", employee));

    /// <summary><b>F (CI).</b> QUAL-267. HR created a person through the admin route (profile from Monday 2 February 2026), then
    /// closed the profile on Tuesday 31 March through the DELETE route (204); the person stays active. The seeder runs. The row set
    /// is identical before and after (nothing written, the closed row untouched), and no new <c>EmployeeProfileCreated</c>
    /// event, no <c>SYSTEM_SEED</c> event and no new audit row is written for the person (the create and the close already wrote theirs).
    /// <para><b>Red conditions.</b> At <c>6790ee63</c> the seeder finds no OPEN row and writes <c>[0001-01-01, open)</c> beside
    /// the closed one: the row-set comparison fails, as do the event and audit counts.</para></summary>
    [Fact]
    public async Task ClosedProfile_ActiveUser_SeederWritesNothing()
    {
        var (hostA, adminA) = HostAt(Created);
        var employee = await CreateUserAsync(adminA, "AC");
        var (hostB, adminB) = HostAt(CloseClock);
        await CloseProfileAsync(adminB, employee);
        var rowsBefore = await ProfileRowsAsync(employee);
        Assert.Matches(@"^[0-9a-f-]{36}\|2026-02-02\|2026-03-31\|\d+\|1\.000\|-$", rowsBefore ?? "");   // the one closed row, not year 1
        var eventsBefore = await ProfileEventCountAsync(employee);
        var auditBefore = await ProfileAuditCountAsync(employee);
        Assert.Equal(1L, eventsBefore);   // the admin create's own event

        await RunSeederAsync(hostB);

        Assert.Equal(rowsBefore, await ProfileRowsAsync(employee));
        Assert.Equal(0L, await ProfileEventCountAsync(employee, "SYSTEM_SEED"));
        Assert.Equal(eventsBefore, await ProfileEventCountAsync(employee));
        Assert.Equal(auditBefore, await ProfileAuditCountAsync(employee));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM employee_profile_audit WHERE employee_id = @e AND actor_id = 'SYSTEM_SEED'", ("e", employee)));
    }

    /// <summary><b>R (CI).</b> An active person with no <c>employee_profiles</c> row at all: the seeder writes exactly one row,
    /// <c>[0001-01-01, open)</c>, fraction 1.000, position null, with its <c>EmployeeProfileCreated</c> event (actor
    /// <c>SYSTEM_SEED</c>) and its <c>CREATED</c> audit row at version 1, as before.
    /// <para><b>Red conditions (kind R).</b> A seeder that skipped a person with no rows (an over-correction of the QUAL-267 fix,
    /// e.g. selecting nobody) writes no row, so the row assertion fails. Green before and after TASK-14849, by design.</para></summary>
    [Fact]
    public async Task NoProfileRowAtAll_SeederWritesTheYearOneRow()
    {
        var (host, _) = HostAt(Created);
        var employee = "usr_s148_seed_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, employee, Org, "AC", "OK24");
        await ExecAsync("DELETE FROM employee_profiles WHERE employee_id = @e", ("e", employee));
        Assert.Equal("", await ProfileRowsAsync(employee));

        await RunSeederAsync(host);

        var rows = await ProfileRowsAsync(employee);
        Assert.Matches(@"^[0-9a-f-]{36}\|0001-01-01\|open\|1\|1\.000\|-$", rows ?? "");   // exactly one row
        Assert.Equal(1L, await ProfileEventCountAsync(employee));
        Assert.Equal(1L, await ProfileEventCountAsync(employee, "SYSTEM_SEED"));
        Assert.Equal(1L, await ProfileAuditCountAsync(employee));
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM employee_profile_audit WHERE employee_id = @e AND action = 'CREATED' " +
            "AND actor_id = 'SYSTEM_SEED' AND version_after = 1", ("e", employee)));
    }

    /// <summary><b>F (CI).</b> QUAL-265 meets QUAL-267. A payable HK person is created through the admin route (profile from
    /// 2 February 2026, February and March 2026 APPROVED with weekday hours), and HR closes the profile on 31 March. The seeder runs, then the
    /// calculation and export of March through the payroll route answers 422 <c>payroll-employment-record-gap</c> (three members
    /// byte for byte) and writes nothing: no export record, no <c>PayrollExportGenerated</c>, no manifest.
    /// <para><b>Red conditions.</b> At <c>6790ee63</c> the seeder's year-1 row covers 31 March, so the record has no gap, the
    /// route answers 200 and the status assertion in <c>AssertRecordGapAsync</c> fails.</para></summary>
    [Fact]
    public async Task ClosedProfile_AfterSeederRun_PayrollStillRefusesTheMonth()
    {
        var (hostA, adminA) = HostAt(Created);
        await TestFixtures.SeedWageTypeMappingsAsync(Db);
        var payroll = PayrollHost.Start(ConnectionString);
        _payrolls.Add(payroll);
        var employee = await CreateUserAsync(adminA, "HK");
        // February too: payroll refuses March until the previous month is approved (409 payroll-prior-month-not-approved).
        foreach (var (start, end) in new[] { (Feb01, Feb28), (Mar01, Mar31) })
        {
            await SeedApprovedMonthAsync(ConnectionString, employee, Org, start, end, "HK");
            await SeedWeekdayHoursAsync(ConnectionString, employee, start, end, "HK");
        }
        var (hostB, adminB) = HostAt(CloseClock);
        await CloseProfileAsync(adminB, employee);

        await RunSeederAsync(hostB);
        using var response = await payroll.ExportAsync(employee, Org, Mar01, Mar31);

        await PayrollEmploymentRecordGapTests.AssertRecordGapAsync(response, employee);
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", employee)));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'PayrollExportGenerated'", ("s", $"employee-{employee}")));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", employee)));
    }
}
