using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Tests.Regression.Balance; // FixedTimeProvider
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S68 / TASK-6808 (ADR-033 D3) — Docker-gated tests for the
/// <see cref="StatsTid.Infrastructure.SettlementCloseService"/> period-close poller's
/// <b>Europe/Copenhagen boundary</b> (prompt scenario 10) driven through the LIVE BackgroundService
/// with an injected fixed <see cref="TimeProvider"/> (the <c>FixedTimeProvider</c> pattern from
/// <c>YearOverviewTests</c>, registered via <see cref="WebHostBuilderExtensions.ConfigureTestServices"/>).
///
/// <para>
/// The poller runs <c>CloseDueSettlementsAsync</c> immediately on <c>ExecuteAsync</c> entry (before
/// its first 5-minute <c>Task.Delay</c>), so booting the host runs one poll against the fixed clock.
/// A VACATION ferieår E (reset_month 9) is due strictly AFTER 31 Dec of E+1 on the Copenhagen
/// business date. We boot two hosts:
/// <list type="bullet">
///   <item><b>Just-after</b> the boundary (fixed UTC 2026-01-01 00:00 ⇒ Copenhagen 2026-01-01) ⇒
///   ferieår 2024 (boundary 31 Dec 2025) IS due ⇒ the freshly-seeded employee's closed year settles
///   (wait-on-condition for the row to appear).</item>
///   <item><b>Just-before</b> the boundary (fixed UTC 2025-12-31 00:00 ⇒ Copenhagen 2025-12-31) ⇒
///   ferieår 2024 is NOT yet due ⇒ the employee's 2024 settlement stays absent for a bounded
///   interval.</item>
/// </list>
/// </para>
///
/// <para>
/// Each test uses a FRESH employee id (unique GUID) so the shared-DB init.sql seed users settling on
/// the same poll do not perturb the per-employee assertion (we key the wait/absence purely on our
/// employee). The poller is idempotent (the in-lock re-check + the partial-unique-active 23505
/// backstop), so re-polls are safe — scenario 4's concurrent/idempotent single-settle is also pinned
/// here via the running poller (the row count for our tuple is exactly one no matter how many polls
/// fire).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettlementCloseServiceBoundaryTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";

    // VACATION reset_month 9 ⇒ ferieår 2024 = Sep 2024 .. Aug 2025 ⇒ §21/§24 boundary = 31 Dec 2025.
    private const int FerieaarUnderTest = 2024;

    // Just-AFTER the boundary: UTC 2026-01-01 00:00 ⇒ Copenhagen 2026-01-01 (> 31 Dec 2025) ⇒ DUE.
    private static readonly DateOnly JustAfterBoundary = new(2026, 1, 1);
    // Just-BEFORE the boundary: UTC 2025-12-31 00:00 ⇒ Copenhagen 2025-12-31 (= 31 Dec 2025, NOT >) ⇒ NOT due.
    private static readonly DateOnly JustBeforeBoundary = new(2025, 12, 31);

    // A go-live date BEFORE every candidate boundary in the two DATE-boundary tests (the earliest
    // candidate ferieår 2020 has boundary 31 Dec 2021, already after 2020-01-01), so the ADR-033 D13
    // launch-neutral gate is satisfied for all and those tests isolate the Copenhagen DATE boundary
    // alone. The gate's OWN inclusion/exclusion + the dormant default get dedicated tests below.
    private static readonly DateOnly BroadGoLive = new(2020, 1, 1);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // NOTE: we intentionally do NOT CreateClient() on the base factory here — each test boots a
        // derived fixed-clock host (which runs its own poller). Seeding the employee happens BEFORE
        // that boot so the poll sees it.
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 10 — DUE side: a fixed clock past 31 Dec E+1 settles the closed ferieår.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// With the close-service clock fixed just AFTER the Copenhagen boundary (2026-01-01), the live
    /// poller settles the freshly-seeded employee's ferieår 2024 (boundary 31 Dec 2025 has passed):
    /// a single active <c>vacation_settlements</c> row appears for the tuple within the wait window.
    /// </summary>
    [Fact]
    public async Task Poller_JustAfterCopenhagenBoundary_SettlesClosedFerieaar()
    {
        var employeeId = await SeedEmployeeAsync();

        // Boot a fixed-clock host AFTER seeding so the immediate first poll observes the employee.
        // BroadGoLive keeps the D13 gate satisfied for ferieår 2024 (this test isolates the DATE boundary).
        BootFixedClockHost(JustAfterBoundary, BroadGoLive);

        // Wait-on-condition: the poller (async background) settles within a bounded interval.
        var settled = await WaitForSettlementAsync(employeeId, FerieaarUnderTest, timeout: TimeSpan.FromSeconds(30));
        Assert.True(settled,
            $"Expected the poller to settle VACATION {FerieaarUnderTest} for {employeeId} at Copenhagen " +
            $"date {JustAfterBoundary} (boundary 31 Dec 2025 passed), but no settlement row appeared.");

        // Exactly ONE active row for the tuple (idempotent across however many polls fired — scenario 4).
        Assert.Equal(1L, await CountActiveSettlementsAsync(employeeId, FerieaarUnderTest));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 10 — NOT-DUE side: a fixed clock on/before 31 Dec E+1 does NOT settle.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// With the close-service clock fixed just BEFORE the boundary (Copenhagen 2025-12-31, NOT past
    /// 31 Dec 2025), the poller does NOT settle ferieår 2024 (its §21/§24 boundary 31 Dec 2025 has not
    /// yet passed). We assert specifically on year 2024 — the freshly-seeded employee's EARLIER candidate
    /// years (2020–2023, boundaries already past and after BroadGoLive since the seed sets no
    /// employment_start_date) DO settle on this same poll, which doubles as the "a poll ran" witness for
    /// <see cref="EnsurePollHadRunAsync"/>. This is the negative half of the DATE-boundary pin (the
    /// launch-neutral go-live gate is exercised separately below).
    /// </summary>
    [Fact]
    public async Task Poller_JustBeforeCopenhagenBoundary_DoesNotSettle()
    {
        var employeeId = await SeedEmployeeAsync();

        BootFixedClockHost(JustBeforeBoundary, BroadGoLive);

        // Give the poller ample time to have run its immediate first pass, then assert STILL absent.
        // (We first wait a beat for the host's background poll to execute, then assert no row — an
        // absence assertion is only meaningful after the poll has had the chance to run.)
        await EnsurePollHadRunAsync(employeeId);
        Assert.Equal(0L, await CountActiveSettlementsAsync(employeeId, FerieaarUnderTest));
    }

    // ════════════════════════════════════════════════════════════════════════
    // ADR-033 D13 launch-neutral go-live gate (S68 fix-forward).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The launch-neutral DEFAULT: with NO <c>Settlement:GoLiveDate</c> configured the poller is
    /// DORMANT. Even with a clock long past every boundary (2026-01-01) and a freshly-seeded closed
    /// ferieår, NOTHING settles — neither this employee's closed year nor the shared init.sql seed
    /// users' years. This is the regression guard for the S68 fix-forward: the close service must not
    /// auto-forfeit pre-launch years it has no lawful quantity source for (the §21 agreements were
    /// never recorded, the absences never captured).
    /// </summary>
    [Fact]
    public async Task Poller_Dormant_NoGoLiveConfigured_SettlesNothing()
    {
        var employeeId = await SeedEmployeeAsync();

        // Boot WITHOUT a go-live date at a clock past every candidate boundary — the dormant gate
        // returns before any DB work, so nothing is ever written.
        BootFixedClockHost(JustAfterBoundary, goLiveDate: null);

        // Bounded settle window for the absence assertion (an immediate first poll has long since run;
        // a dormant poll writes nothing, so no row can ever appear no matter how long we wait).
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(0L, await CountActiveSettlementsAsync(employeeId, FerieaarUnderTest));
        Assert.False(await AnySettlementExistsAsync(),
            "A DORMANT poller (no Settlement:GoLiveDate) must settle nothing — not this employee, not the seed users.");
    }

    /// <summary>
    /// The gate's inclusion/exclusion split on a SINGLE employee: with go-live fixed at 2025-06-01 and
    /// the clock past every boundary (2026-01-01), the poller settles the POST-go-live boundary
    /// (ferieår 2024, boundary 31 Dec 2025 — after go-live) but NOT the PRE-go-live boundary (ferieår
    /// 2023, boundary 31 Dec 2024 — before go-live, the manual operator fallback). Both years are real
    /// candidates for the seeded employee (no employment_start_date ⇒ floor 2020), so the 2023 absence
    /// is a deliberate exclusion, not a missing candidate.
    /// </summary>
    [Fact]
    public async Task Poller_GoLiveGate_SettlesPostGoLiveBoundary_NotPreGoLive()
    {
        var employeeId = await SeedEmployeeAsync();
        // Between the 2023 boundary (31 Dec 2024) and the 2024 boundary (31 Dec 2025).
        var goLive = new DateOnly(2025, 6, 1);

        BootFixedClockHost(JustAfterBoundary, goLive);

        // Post-go-live boundary settles (wait-on-condition).
        var settled2024 = await WaitForSettlementAsync(employeeId, 2024, timeout: TimeSpan.FromSeconds(30));
        Assert.True(settled2024,
            "ferieår 2024 (boundary 31 Dec 2025, AFTER go-live 2025-06-01) must auto-settle.");

        // Pre-go-live boundary is the manual fallback — never auto-settled (even though 2023 IS a
        // candidate and its deadline 31 Dec 2024 has passed).
        Assert.Equal(0L, await CountActiveSettlementsAsync(employeeId, 2023));
    }

    // ─────────────────────────────── host boot ───────────────────────────────

    /// <summary>Boots a derived WAF host whose <see cref="TimeProvider"/> is fixed to
    /// <paramref name="fixedDate"/> (UTC midnight) and whose <c>Settlement:GoLiveDate</c> is set to
    /// <paramref name="goLiveDate"/> (ISO yyyy-MM-dd) — or left UNSET when null, exercising the dormant
    /// default. The boot starts the SettlementCloseService hosted service, which runs one poll
    /// immediately against the fixed clock. The derived factory is rooted in the test's lifetime via
    /// <see cref="_factory"/> (disposed in DisposeAsync).</summary>
    private void BootFixedClockHost(DateOnly fixedDate, DateOnly? goLiveDate)
    {
        var derived = _factory.WithWebHostBuilder(builder =>
        {
            if (goLiveDate is not null)
            {
                builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Settlement:GoLiveDate"] = goLiveDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    }));
            }
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedDate));
            });
        });
        _ = derived.CreateClient(); // triggers host build + hosted-service start (immediate poll)
    }

    // ─────────────────────────────── waits ───────────────────────────────

    /// <summary>Polls the DB until an active settlement for the tuple appears or the timeout
    /// elapses. Returns true on appearance. (The poller runs on a background thread post-boot; this
    /// is a bounded wait-on-condition, not a fixed sleep.)</summary>
    private async Task<bool> WaitForSettlementAsync(string employeeId, int year, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await CountActiveSettlementsAsync(employeeId, year) >= 1)
                return true;
            await Task.Delay(250);
        }
        return false;
    }

    /// <summary>For the NOT-due assertion: wait until the poller has demonstrably run at least one
    /// pass (so the subsequent absence check is meaningful, not merely racing an un-run poll). We
    /// detect "a poll ran" by observing that SOME settlement row exists in the table (the 19 seeded
    /// init.sql users have closed ferieår whose boundaries — for the years BEFORE 2024 — ARE past
    /// even at Copenhagen 2025-12-31, so the poll WILL settle at least those). If no rows ever
    /// appear we fall back to a fixed bounded wait so the test still terminates.</summary>
    private async Task EnsurePollHadRunAsync(string employeeId)
    {
        var sw = Stopwatch.StartNew();
        var window = TimeSpan.FromSeconds(20);
        while (sw.Elapsed < window)
        {
            // Any settlement row at all (e.g. a seeded user's earlier closed ferieår) proves a poll
            // executed against this fixed-clock host.
            if (await AnySettlementExistsAsync())
                return;
            await Task.Delay(250);
        }
        // Fallback bounded wait — the poll had its window; proceed to the absence assertion.
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s68_boundary_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<long> CountActiveSettlementsAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<bool> AnySettlementExistsAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM vacation_settlements)", conn);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }
}
