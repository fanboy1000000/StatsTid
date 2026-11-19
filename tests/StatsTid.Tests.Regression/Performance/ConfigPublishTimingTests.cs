using System.Diagnostics;
using System.Net;
using StatsTid.Tests.Regression.Config;
using Xunit.Abstractions;

namespace StatsTid.Tests.Regression.Performance;

/// <summary>
/// S148 / TASK-14823-T (the publish timing figure; plan O-15, "an F-row if the publish measured slow") - how long a
/// large central publish holds the system-wide "pay inputs" lock.
///
/// <para>
/// <b>In plain language.</b> An agreement-config publish takes the pay-inputs lock first and keeps it until its
/// transaction commits, and in that transaction it lists on HR's worklist every paid month it reaches for every
/// employee on the agreement code. While it runs, payroll recordings and the other configuration writes wait. This
/// fact measures one big publish (300 people on HK, each with January to April 2026 paid) and prints the rows it
/// landed and its wall-clock time. The time bounds how long the lock was held: the request is the transaction plus
/// a few milliseconds of HTTP. It is a MEASUREMENT, recorded for the Orchestrator to read from CI's results; no
/// time is asserted, because a fixed ceiling on a shared CI machine would be a flaky test, not a finding. A slow
/// figure becomes a performance-register row.
/// </para>
///
/// <para>
/// <b>Method.</b> The people are made by bulk SQL (<c>generate_series</c>, as <see cref="S106SeedScalePerfTests"/>
/// does), the clock is fixed at Tuesday 10 November 2026, one warm-up publish of another key (HK OK26 dated Monday
/// 9 November 2026, which reaches only November, where nothing is exported, so it lands no row) pays the JIT and
/// plan-cache cost, then ONE measured publish of HK OK24 dated Monday 5 January 2026 (reach January to April:
/// 300 people times 4 months). A single publish, not a median of five: each publish needs a fresh draft dated not
/// before the last, and a publish's cost is its row count, which does not vary between runs.
/// </para>
///
/// <para>
/// <b>Kind: F (CI)</b> for the row count (before TASK-14823 a publish lands no row, so the count is 0, not 1,200).
/// The time is a recording. Whether the printed line reaches the trx file CI uploads is settled by the gate's trx
/// check, not by this fact. Docker-gated: CI-pending.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ConfigPublishTimingTests : DatedConfigDockerFixture
{
    private const int Persons = 300;
    private const int ExportedMonths = 4;
    private const string Prefix = "usr_s148_perf_";

    private static readonly DateOnly Clock = new(2026, 11, 10);

    private readonly ITestOutputHelper _out;

    public ConfigPublishTimingTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// <b>F (CI)</b> (row count; the time is recorded, never asserted). 300 HK people each with January to April 2026
    /// exported; the warm-up publish lands no row; the measured HK OK24 publish dated Monday 5 January 2026 answers
    /// 200 and lands exactly 1,200 EXPORTED_MONTH rows (300 people times January, February, March, April), 300 per
    /// month. Prints one line: people, months, rows landed and the request's wall-clock milliseconds.
    /// <para><b>Red conditions.</b> Before TASK-14823 the publish lands no row: the 1,200 assertion fails with 0.
    /// A reach that lost a month or a person fails the per-month assertion.</para>
    /// </summary>
    [Fact]
    public async Task LargeCentralPublish_RowCountAndHold_Recorded()
    {
        var admin = AdminAt(Clock);
        await SeedPeopleAsync();

        // Warm-up: another key, reaching only November (nothing exported there).
        var (warmId, warmVersion) = await CreateDraftAsync(admin, ConfigBody("HK", "OK26"));
        await PublishOkAsync(admin, warmId, warmVersion, "2026-11-09");
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist"));

        var (configId, version) = await CreateDraftAsync(admin, ConfigBody("HK", "OK24"));
        var stopwatch = Stopwatch.StartNew();
        using var response = await PublishAsync(admin, configId, version, "2026-01-05");
        stopwatch.Stop();
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"the measured publish expected 200, got {(int)response.StatusCode}: {text}");

        var rows = await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE kind = 'EXPORTED_MONTH'");
        _out.WriteLine(
            $"S148 CONFIG_PUBLISH_TIMING persons={Persons} exportedMonths={ExportedMonths} rowsLanded={rows} " +
            $"publishWallClockMs={stopwatch.ElapsedMilliseconds}");

        Assert.Equal((long)Persons * ExportedMonths, rows);
        for (var month = 1; month <= ExportedMonths; month++)
        {
            Assert.Equal((long)Persons, await CountAsync(
                "SELECT COUNT(*) FROM hr_backdate_worklist WHERE kind = 'EXPORTED_MONTH' AND year = 2026 AND month = @m",
                ("m", month)));
        }
    }

    /// <summary>300 HK people in STY01 (users, an open profile, the organisation row from the literal
    /// <c>'0001-01-01'</c>, the agreement-code row) and January to April 2026 exported for each, by four set-based
    /// statements, the shape <c>RegressionSeed</c> writes one person at a time.</summary>
    private async Task SeedPeopleAsync()
    {
        await ExecAsync(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version)
            SELECT @prefix || g, @prefix || g, 'dev-only', 'Perf ' || g, NULL, 'STY01', 'HK', 'OK24'
            FROM generate_series(1, @n) AS g
            """, ("prefix", Prefix), ("n", Persons));
        await ExecAsync(
            """
            INSERT INTO employee_profiles
                (profile_id, employee_id, part_time_fraction, position, effective_from, effective_to, version, employment_category)
            SELECT gen_random_uuid(), u.user_id, 1.000, NULL, '0001-01-01', NULL, 1, u.employment_category
            FROM users u WHERE starts_with(u.user_id, @prefix)
            """, ("prefix", Prefix));
        await ExecAsync(
            """
            INSERT INTO user_organisations (assignment_id, user_id, org_id, effective_from, effective_to, version)
            SELECT gen_random_uuid(), u.user_id, 'STY01', '0001-01-01', NULL, 1
            FROM users u WHERE starts_with(u.user_id, @prefix)
            """, ("prefix", Prefix));
        await ExecAsync(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            SELECT gen_random_uuid(), u.user_id, 'HK', '0001-01-01', NULL, 1
            FROM users u WHERE starts_with(u.user_id, @prefix)
            """, ("prefix", Prefix));
        await ExecAsync(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month, original_lines, current_effective_lines, content_hash)
            SELECT gen_random_uuid(), NULL, u.user_id, 2026, m, '[]'::jsonb, '[]'::jsonb, 'hash-2026-' || m
            FROM users u CROSS JOIN generate_series(1, 4) AS m
            WHERE starts_with(u.user_id, @prefix)
            """, ("prefix", Prefix));
    }
}
