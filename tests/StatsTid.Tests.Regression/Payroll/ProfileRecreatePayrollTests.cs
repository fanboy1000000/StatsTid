using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using StatsTid.Tests.Regression.Concurrency;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Concurrency.PayInputsLockTestKit;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14851-T (AC-Q268h; QUAL-268; answers QUAL-266's open question) - what payroll says after HR closes a profile and re-creates it
/// from a later date: the worked example.
///
/// <para>
/// <b>In plain language.</b> HR closes Anna's profile on Monday 16 March 2026 and, on Friday 10 April, re-creates it from Wednesday 25 March.
/// The days from the 16th to the 24th now have no profile, so payroll refuses March by name (422) and writes nothing; April, which lies wholly
/// inside the new profile, is paid on the new fraction. HR then fills the gap with an ordinary edit dated 16 March, and March is paid, cut
/// into pieces at the 16th and the 25th.
/// </para>
///
/// <para>
/// <b>Fixture.</b> One database, two Backend hosts (clocks Monday 16 March and Friday 10 April 2026) and a payroll host
/// (<see cref="PayInputsLockTestKit"/>); Anna (HK / OK24, STY01) has February, March and April 2026 APPROVED with weekday hours recorded.
/// The 422 body is asserted by <see cref="PayrollEmploymentRecordGapTests.AssertRecordGapAsync"/>. Docker-gated: <b>CI-pending</b>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileRecreatePayrollTests : DatedConfigDockerFixture, IDisposable
{
    private const string Org = "STY01";

    private static readonly DateOnly Feb01 = new(2026, 2, 1);
    private static readonly DateOnly Feb28 = new(2026, 2, 28);
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar16 = new(2026, 3, 16);   // a Monday
    private static readonly DateOnly Mar25 = new(2026, 3, 25);   // a Wednesday
    private static readonly DateOnly Mar30 = new(2026, 3, 30);   // a Monday
    private static readonly DateOnly Mar31 = new(2026, 3, 31);
    private static readonly DateOnly Apr01 = new(2026, 4, 1);
    private static readonly DateOnly Apr10 = new(2026, 4, 10);   // a Friday
    private static readonly DateOnly Apr30 = new(2026, 4, 30);

    private readonly List<WebApplicationFactory<Program>> _hosts = new();
    private readonly List<PayrollHost> _payrolls = new();

    public void Dispose()
    {
        foreach (var payroll in _payrolls)
            payroll.DisposeAsync().AsTask().GetAwaiter().GetResult();
        foreach (var host in _hosts)
            host.Dispose();
    }

    private Task<long> VersionAsync(string employee) =>
        ScalarAsync<long>("SELECT version FROM users WHERE user_id = @u", ("u", employee));

    private Task<long> RecordCountAsync(string employee, int month) => CountAsync(
        "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e AND year = 2026 AND month = @m", ("e", employee), ("m", month));

    /// <summary>
    /// <b>F (CI).</b> The worked example (AC-Q268h). (1) Close on Monday 16 March 2026 (204). (2) Re-create on Friday 10 April 2026 from Wednesday
    /// 25 March, 0.800 (200). (3) Export March: 422 <c>payroll-employment-record-gap</c>, nothing written (no March record). (4) Export April: 200, one
    /// April record, and the month's frozen inputs (<c>segment_manifests.plan_inputs_jsonb</c>, the manifest of the April calculation) show every piece
    /// that reaches 25 March or later at fraction 0.800, with a piece inside April; this settles TASK-14851's <c>(unproven)</c> that April's opening-week
    /// days (Monday 30 and Tuesday 31 March) are covered. (5) The gap filled: an edit at Friday 10 April dated Monday 16 March (<c>If-Match</c>,
    /// fraction 1.000, position null) answers 200 and the rows are <c>[0001-01-01, 03-16)</c>, <c>[03-16, 03-25)</c>, <c>[03-25, open)</c>. (6) Export March:
    /// 200, one March record, with pieces starting on 16 March and on 25 March (<c>Engine.LatestTimelinePieces()</c>).
    /// <para><b>Product code between the readings:</b> the close, the re-create, the edit's gap insert (router case G), payroll's planner and its
    /// record-coverage check.</para>
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> step 2 answers 428, so its status assertion fails first. Steps 3-6 also answer QUAL-266's open
    /// question ("whether a profile PUT backdated over the gap can fill it once the later row exists"): step 5 and step 6 show it can.</para></summary>
    [Fact]
    public async Task CloseThenRecreateFrom25Mar_March422_April200_GapFilledMarch200()
    {
        var closeHost = Factory.WithFixedToday(Mar16);
        var recreateHost = Factory.WithFixedToday(Apr10);
        _hosts.Add(closeHost);
        _hosts.Add(recreateHost);
        var closer = LocalHrClient(closeHost, "HR_S148_Q268_PAY", "STY01"); // boots the host before anything is seeded (PAT-008)
        var lise = LocalHrClient(recreateHost, "HR_S148_Q268_PAY", "STY01");
        await TestFixtures.SeedWageTypeMappingsAsync(Db);
        var payroll = PayrollHost.Start(ConnectionString);
        _payrolls.Add(payroll);
        var anna = await SeedPayableEmployeeAsync(ConnectionString, Org, (Feb01, Feb28), (Mar01, Mar31), (Apr01, Apr30));

        // 1. the close
        var close = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{anna}");
        close.Headers.TryAddWithoutValidation("If-Match", $"\"{await VersionAsync(anna)}\"");
        using (var closed = await closer.SendAsync(close))
            Assert.True(closed.StatusCode == HttpStatusCode.NoContent, $"the close: {(int)closed.StatusCode} {await closed.Content.ReadAsStringAsync()}");

        // 2. the re-create from 25 March
        var create = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{anna}")
        {
            Content = JsonContent.Create(new { effectiveFrom = "2026-03-25", partTimeFraction = 0.800m, position = (string?)null, employmentCategory = (string?)null }),
        };
        create.Headers.TryAddWithoutValidation("If-None-Match", "*");
        using (var created = await lise.SendAsync(create))
            Assert.True(created.StatusCode == HttpStatusCode.OK, $"the re-create: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");

        // 3. March: 422, nothing written
        using (var march = await payroll.ExportAsync(anna, Org, Mar01, Mar31))
            await PayrollEmploymentRecordGapTests.AssertRecordGapAsync(march, anna);
        Assert.Equal(0L, await RecordCountAsync(anna, 3));

        // 4. April: 200, paid on the new profile
        using (var april = await payroll.ExportAsync(anna, Org, Apr01, Apr30))
            Assert.True(april.StatusCode == HttpStatusCode.OK, $"the April export: {(int)april.StatusCode} {await april.Content.ReadAsStringAsync()}");
        Assert.Equal(1L, await RecordCountAsync(anna, 4));
        var inputs = await ScalarAsync<string>(
            "SELECT plan_inputs_jsonb::text FROM segment_manifests WHERE employee_id = @e AND period_start = DATE '2026-04-01' ORDER BY created_at DESC LIMIT 1",
            ("e", anna));
        Assert.NotNull(inputs);
        using (var doc = JsonDocument.Parse(inputs!))
        {
            var pieces = doc.RootElement.GetProperty("pieces").EnumerateArray()
                .Select(p => (
                    From: DateOnly.Parse(p.GetProperty("from").GetString()!, CultureInfo.InvariantCulture),
                    To: DateOnly.Parse(p.GetProperty("to").GetString()!, CultureInfo.InvariantCulture),
                    Fraction: p.GetProperty("partTimeFraction").GetDecimal()))
                .ToList();
            Assert.Contains(pieces, p => p.From >= Apr01 && p.To <= Apr30);
            var onNewProfile = pieces.Where(p => p.To >= Mar25).ToList();
            Assert.NotEmpty(onNewProfile);
            Assert.All(onNewProfile, p => Assert.Equal(0.800m, p.Fraction));
            // April's opening week: one piece covers Monday 30 and Tuesday 31 March at 0.800.
            Assert.Contains(pieces, p => p.From <= Mar30 && p.To >= Mar31 && p.Fraction == 0.800m);
        }

        // 5. the gap filled by an ordinary edit dated 16 March
        var edit = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{anna}")
        {
            Content = JsonContent.Create(new { effectiveFrom = "2026-03-16", partTimeFraction = 1.000m, position = (string?)null, employmentCategory = (string?)null }),
        };
        edit.Headers.TryAddWithoutValidation("If-Match", $"\"{await VersionAsync(anna)}\"");
        using (var filled = await lise.SendAsync(edit))
            Assert.True(filled.StatusCode == HttpStatusCode.OK, $"the gap-filling edit: {(int)filled.StatusCode} {await filled.Content.ReadAsStringAsync()}");
        var rows = await ScalarAsync<string>(
            """
            SELECT string_agg(
                CASE WHEN effective_from = '-infinity' THEN '0001-01-01' ELSE effective_from::text END
                    || '|' || COALESCE(effective_to::text, 'open'), ';' ORDER BY effective_from)
            FROM employee_profiles WHERE employee_id = @e
            """, ("e", anna));
        Assert.Equal("0001-01-01|2026-03-16;2026-03-16|2026-03-25;2026-03-25|open", rows);

        // 6. March: 200, pieces from 16 and 25 March
        using (var march = await payroll.ExportAsync(anna, Org, Mar01, Mar31))
            Assert.True(march.StatusCode == HttpStatusCode.OK, $"the March export after the gap fill: {(int)march.StatusCode} {await march.Content.ReadAsStringAsync()}");
        Assert.Equal(1L, await RecordCountAsync(anna, 3));
        var starts = payroll.Engine.LatestTimelinePieces().Select(p => p.From).ToList();
        Assert.Contains(Mar16, starts);
        Assert.Contains(Mar25, starts);
    }
}
