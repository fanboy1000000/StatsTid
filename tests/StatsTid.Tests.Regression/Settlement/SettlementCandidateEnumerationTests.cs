using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S141 / TASK-14103 (Part A4, QUAL-168) — Docker-gated pins for
/// <see cref="StatsTid.Infrastructure.SettlementCloseService"/>'s candidate-year enumeration for a
/// mid-ferieår hire.
///
/// <para>
/// <b>The plain-language problem.</b> Before the poller can settle anyone's holiday year it first
/// has to work out WHICH years to even consider for them — its "candidate list". For VACATION that
/// list's lower bound is computed from <c>employment_start_date</c>, but today it uses the RAW
/// calendar year of the hire, while the upper bound (used for leavers) already converts a date into
/// its correct holiday year via "month ≥ September ⇒ that year, else the year before". For an
/// employee hired January-through-August, that mismatch means their FIRST (partial) holiday year —
/// the one that started the September before they were hired — is never even considered a candidate,
/// so it can never be settled. QUAL-168 gives the lower bound the SAME month-based mapping the upper
/// bound already has.
/// </para>
///
/// <para>
/// <b>Why this is tested through the LIVE poller, not a unit test.</b> The enumeration method is
/// private — the only way to observe its output is to run the real background poller against a real
/// database and see which settlement rows it produces, exactly as
/// <see cref="SettlementCloseServiceBoundaryTests"/> already does for the boundary question.
/// </para>
///
/// <para>
/// <b>Isolated from TASK-14101's anchor question.</b> Every employee below has dated
/// <c>employee_profiles</c> / <c>user_agreement_codes</c> history OPEN SINCE YEAR 1 (the
/// RegressionSeed default) — never anchored to the hire date — so a dated read always succeeds
/// regardless of which anchor a capture uses. That means the ONLY thing that can make a settlement
/// row appear or not appear here is which years the ENUMERATION offers up as candidates, not whether
/// the capture itself can find a dated record.
/// </para>
///
/// <para>
/// <b>RED-FIRST, reasoned from the spec.</b> Docker is unavailable on the authoring machine — these
/// first execute (and are CI-verified, never claimed green locally) in the sprint-close watched CI
/// run.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettlementCandidateEnumerationTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";
    private const string SpecialHolidayType = "SPECIAL_HOLIDAY";

    private static readonly DateOnly BroadGoLive = new(2020, 1, 1);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Deliberately NOT booted here (PAT-008 "one host per fact") — seeding happens before each
        // fact boots its own fixed-clock host, so the poller's immediate first pass observes it.
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Fact 1 — the FIX: a January-August hire's first ferieår is now a candidate.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>What this proves.</b> An employee hired 10 Mar 2026 — inside ferieår 2025 (1 Sep 2025 –
    /// 31 Aug 2026), five months after it started — has that ferieår as a candidate for settlement.
    /// </summary>
    /// <remarks>
    /// <b>RED today.</b> Today's lower bound is the raw calendar year of the hire (2026), so ferieår
    /// 2025 is never even offered as a candidate for this employee, and the wait below times out with
    /// no row ever appearing — no matter how far in the future the clock is fixed. <b>GREEN</b> once
    /// QUAL-168's fix gives the lower bound the same "month ≥ 9 ⇒ this year, else the year before"
    /// mapping the leaver upper bound already has (2026, month 3 ⇒ 2025).
    /// </remarks>
    [Fact]
    public async Task Poller_JanuaryToAugustHire_FirstFerieaarBecomesACandidate()
    {
        var employeeId = await SeedEmployeeAsync("emp_qual168_janaug");
        await SetEmploymentStartDateAsync(employeeId, new DateOnly(2026, 3, 10));

        // Clock fixed well past ferieår 2025's §21 boundary (31 Dec 2026), so if it is a candidate
        // at all it is also DUE.
        BootFixedClockHost(new DateOnly(2027, 1, 2), BroadGoLive);

        var settled = await WaitForSettlementAsync(employeeId, 2025, VacationType, TimeSpan.FromSeconds(30));
        Assert.True(settled,
            "A January-August hire's first (partial) ferieår must be enumerated as a candidate and " +
            "settle, once its boundary has passed — QUAL-168.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Fact 2 — the CONTROL: a September-December hire was already correct; the fix must not disturb it.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>What this proves.</b> An employee hired 15 Oct 2025 — inside the SAME ferieår 2025 the fix
    /// in Fact 1 targets — already resolves to the correct candidate year today, because the raw
    /// calendar year of an October hire (2025) happens to equal the mapped year for a month ≥
    /// September. This fact makes sure QUAL-168's change to the lower-bound EXPRESSION does not
    /// accidentally shift this already-correct case.
    /// </summary>
    /// <remarks>
    /// <b>Already GREEN today, and must STAY green.</b> Unlike Fact 1, this is not a fix-demonstration
    /// — it is a regression guard. A future edit to the lower-bound expression that got the month
    /// comparison or the ±1 direction wrong would make THIS fact fail even though Fact 1 might still
    /// pass, which is exactly the kind of mistake a single "it settles now" pin would miss.
    /// </remarks>
    [Fact]
    public async Task Poller_SeptemberToDecemberHire_FerieaarEnumeration_StaysUnaffected()
    {
        var employeeId = await SeedEmployeeAsync("emp_qual168_sepdec");
        await SetEmploymentStartDateAsync(employeeId, new DateOnly(2025, 10, 15));

        BootFixedClockHost(new DateOnly(2027, 1, 2), BroadGoLive);

        var settled = await WaitForSettlementAsync(employeeId, 2025, VacationType, TimeSpan.FromSeconds(30));
        Assert.True(settled,
            "A September-December hire's own ferieår was already a correct candidate before QUAL-168 " +
            "and must remain one after it.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Fact 3 — SPECIAL_HOLIDAY's OWN enumeration is calendar geometry and is NOT touched by QUAL-168.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>What this proves.</b> SPECIAL_HOLIDAY (særlige feriedage) accrues on the plain calendar
    /// year, so its candidate year for a hire is simply that hire's own calendar year — no
    /// September-based mapping applies or is needed. An employee hired 10 Mar 2021 has accrual year
    /// 2021 as their (correct, and only) candidate; accrual year 2020 — which VACATION's
    /// month-mapping would incorrectly produce for a Jan-Aug hire if it were mistakenly copied onto
    /// this OTHER method — must NOT appear for them.
    /// </summary>
    /// <remarks>
    /// <b>Already GREEN today (QUAL-168 does not touch this method), and stays green after.</b> This
    /// is a regression guard against a plausible future mistake: copying VACATION's newly-fixed
    /// month-based mapping onto SPECIAL_HOLIDAY's OWN candidate-year lower bound, which uses the raw
    /// calendar year deliberately and correctly.
    /// </remarks>
    /// <remarks>
    /// <b>A caveat about the negative half of this pin, recorded rather than hidden.</b> A hire in
    /// Jan-Aug is, by construction, always AFTER 31 December of the year immediately before it — so
    /// if accrual year 2020 were ever wrongly enumerated for this employee, S141's OTHER new guard
    /// (an out-of-period hire fails closed rather than settling at zero — <see cref="SettlementAnchorCaptureTests"/>
    /// Pin 6) would independently catch and discard that attempt before it could write a row. So the
    /// absence of a 2020 row proves EITHER that 2020 was correctly never a candidate, OR that it was
    /// wrongly a candidate but was then correctly rejected by the other guard — this pin's negative
    /// half cannot fully tell those two apart. The POSITIVE half (2021 settles) is unaffected by that
    /// interaction and remains a clean, direct check of this method's own candidate year.
    /// </remarks>
    [Fact]
    public async Task Poller_SpecialHoliday_CandidateYear_IsHiresOwnCalendarYear_NotMonthMapped()
    {
        var employeeId = await SeedEmployeeAsync("emp_qual168_sh");
        await SetEmploymentStartDateAsync(employeeId, new DateOnly(2021, 3, 10));

        // Past BOTH the 2020 and 2021 SPECIAL_HOLIDAY godtgørelse boundaries (30 Apr Y+2).
        BootFixedClockHost(new DateOnly(2024, 1, 1), BroadGoLive);

        var settled2021 = await WaitForSettlementAsync(
            employeeId, 2021, SpecialHolidayType, TimeSpan.FromSeconds(30));
        Assert.True(settled2021,
            "SPECIAL_HOLIDAY's own candidate year for a March-2021 hire is 2021 (the calendar year of " +
            "the hire) — unaffected by QUAL-168, which touches only VACATION's method.");

        Assert.Equal(0L, await CountActiveSettlementsAsync(employeeId, 2020, SpecialHolidayType));
    }

    // ─────────────────────────────── host boot ───────────────────────────────

    /// <summary>Boots a derived WAF host whose <see cref="TimeProvider"/> is fixed and whose
    /// <c>Settlement:GoLiveDate</c> is set — the <c>SettlementCloseServiceBoundaryTests</c>
    /// pattern.</summary>
    private void BootFixedClockHost(DateOnly fixedDate, DateOnly goLiveDate)
    {
        var derived = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Settlement:GoLiveDate"] = goLiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedDate));
            });
        });
        _ = derived.CreateClient(); // triggers host build + hosted-service start (immediate poll).
    }

    // ─────────────────────────────── waits ───────────────────────────────

    private async Task<bool> WaitForSettlementAsync(
        string employeeId, int year, string entitlementType, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await CountActiveSettlementsAsync(employeeId, year, entitlementType) >= 1)
                return true;
            await Task.Delay(250);
        }
        return false;
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    /// <summary>Seeds a fresh employee whose dated profile/agreement history is OPEN SINCE YEAR 1
    /// (the RegressionSeed default) — deliberately NOT anchored to the hire date, so a dated read
    /// always succeeds and only the ENUMERATION can decide whether a row appears.</summary>
    private async Task<string> SeedEmployeeAsync(string prefix)
    {
        var employeeId = prefix + "_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    private async Task SetEmploymentStartDateAsync(string employeeId, DateOnly hire)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_start_date = @d WHERE user_id = @u", conn);
        cmd.Parameters.AddWithValue("d", hire);
        cmd.Parameters.AddWithValue("u", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<long> CountActiveSettlementsAsync(string employeeId, int year, string entitlementType)
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
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
