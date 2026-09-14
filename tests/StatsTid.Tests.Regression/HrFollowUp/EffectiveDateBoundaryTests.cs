using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.HrFollowUp;

/// <summary>
/// S141 / TASK-14105 — Docker-gated pins for refinement <b>B2</b> (the effective-date refresh) and
/// <b>B8</b> (the "no record covers today" detector, and the fail-closed reader's newly caught,
/// named condition).
///
/// <para>
/// <b>THE PROBLEM THESE PIN, in plain language.</b> S141 lets HR enter an employment change in
/// October and date it 1 November. Nothing in this system is triggered by a date ARRIVING — every
/// cache refresh and self-heal fires on a WRITE — so two things used to happen silently on
/// 1 November: the two live caches (<c>users.agreement_code</c>, <c>users.employment_category</c>)
/// kept showing October's answer until somebody wrote to that employee again (B2), and an employee
/// whose ONLY record started in November was covered by nothing today, which made payroll and
/// compliance fail closed for them every day while the one HR list that should have surfaced them
/// filtered them out (B8).
/// </para>
///
/// <para>
/// <b>RED-FIRST, reasoned from the spec — and stated honestly.</b> Docker is unavailable on the
/// authoring machine, so NOTHING here has been observed to run: every fact is derived from
/// <c>DelegationExpiryService.cs</c> / <c>HrFollowUpApprovalReadRepository.cs</c> /
/// <c>ComplianceEndpoints.cs</c> as read. They are CI-verified in the sprint-close watched run and
/// are NOT claimed green locally. Each fact's doc comment states the condition under which it goes
/// RED, because a pin whose failure mode is not written down is a decoration.
/// </para>
///
/// <para>
/// <b>PAT-008 — one fixed anchor, one host per fact.</b> <c>F = 2025-11-12</c>, the same anchor as
/// the sibling HR follow-up suite. <see cref="DelegationExpiryService"/> is a hosted
/// <c>BackgroundService</c> that runs BOTH its sweeps once immediately at host start and then every
/// five minutes, off the same injected <c>TimeProvider</c>. Every fact therefore boots its own host
/// and seeds AFTER <c>CreateClient()</c> has returned, so the startup pass cannot have seen the
/// seeded rows; the manual single-shot call is what the fact actually measures. <b>Declared
/// residual race:</b> the startup pass may still be in flight for a few milliseconds after
/// <c>CreateClient()</c> returns. It cannot corrupt these facts — the refresh is idempotent, so a
/// concurrent pass either does the same write or nothing — but it is recorded rather than hidden.
/// </para>
///
/// <para>
/// <b>★ The two CLOCK facts are the load-bearing ones.</b> Owner ruling, 2026-09-14: the boundary
/// refresh and the "no record covers today" detector both use the WRITERS' UTC DAY, not the
/// Copenhagen business day that the rest of the HR follow-up family uses. The ordinary fixture
/// (<c>WithFixedToday</c>) pins UTC midnight, where the two calendars AGREE — so it cannot tell the
/// two apart, and a pin built on it would pass under either clock. The two clock facts below
/// therefore pin the host at <b>23:30 UTC</b>, which is already the NEXT day in Copenhagen, and are
/// the only construction that can fail if somebody "corrects" either site back to
/// <c>CopenhagenBusinessDate</c>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EffectiveDateBoundaryTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgA = "STY_S141_EDB_A";

    /// <summary>Wednesday, OK24 side — the one fixed "today" for every fact in this class.</summary>
    private static readonly DateOnly F = new(2025, 11, 12);

    /// <summary>
    /// The same calendar day as <see cref="F"/> in UTC, but already <c>F + 1</c> in Copenhagen
    /// (CET = UTC+1 in November). The ONLY instant at which the two "today" definitions this sprint
    /// had to choose between are distinguishable.
    /// </summary>
    private static readonly DateTimeOffset FLateUtcEvening =
        new(F.Year, F.Month, F.Day, 23, 30, 0, TimeSpan.Zero);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Deliberately NOT booted here (PAT-008 "one host per fact").
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private static int Seq;
    private static string NextId(string prefix) => $"{prefix}_{Interlocked.Increment(ref Seq)}";

    // ════════════════════════════════════════════════════════════════════════
    // B2 — the effective-date refresh
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A change scheduled for today: the agreement-code cache follows the row covering today,
    /// <c>users.version</c> moves exactly once, and the move is explained by a SYSTEM-actor
    /// <c>users_audit</c> row carrying both cached fields on both sides.
    ///
    /// <para>RED if the refresh sweep is absent (the cache stays on the superseded code — the whole
    /// B2 defect); if the version bump is dropped (the token would stop meaning "something about
    /// this employee changed"); or if the audit row is omitted (wave 1's invariant that every
    /// <c>users.version</c> transition is explained would break silently).</para>
    /// </summary>
    [Fact]
    public async Task Refresh_ScheduledAgreementChangeTakesEffectToday_FlipsCache_BumpsVersionOnce_WritesSystemAuditRow()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("b2_agr");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA, agreementCode: "AC");
        // The scheduled change, exactly as the writer would have left it when HR entered it in
        // October: the timeline is already split at F, but users.agreement_code still says "AC"
        // because a future-dated write deliberately leaves the cache alone.
        await CloseAgreementCodeRowAsync(employeeId, F);
        await SeedAgreementCodeRowAsync(employeeId, F, null, code: "HK");
        Assert.Equal("AC", await ScalarStringAsync("SELECT agreement_code FROM users WHERE user_id = @p0", employeeId));

        var versionBefore = await ScalarLongAsync("SELECT version FROM users WHERE user_id = @p0", employeeId);
        var auditBefore = await CountAsync("SELECT COUNT(*) FROM users_audit WHERE user_id = @p0", employeeId);

        await RunBoundaryRefreshAsync(host);

        Assert.Equal("HK", await ScalarStringAsync("SELECT agreement_code FROM users WHERE user_id = @p0", employeeId));
        Assert.Equal(versionBefore + 1, await ScalarLongAsync("SELECT version FROM users WHERE user_id = @p0", employeeId));
        Assert.Equal(auditBefore + 1, await CountAsync("SELECT COUNT(*) FROM users_audit WHERE user_id = @p0", employeeId));

        Assert.Equal(1, await CountAsync(
            """
            SELECT COUNT(*) FROM users_audit
            WHERE user_id = @p0
              AND action = 'UPDATED'
              AND actor_id = 'SYSTEM'
              AND actor_role = 'SYSTEM'
              AND previous_data->>'agreementCode' = 'AC'
              AND new_data->>'agreementCode' = 'HK'
              AND version_before = @p1
              AND version_after = @p2
            """, employeeId, versionBefore, versionBefore + 1));
    }

    /// <summary>
    /// The same boundary on the OTHER cache: a scheduled employment-category change flips
    /// <c>users.employment_category</c>.
    ///
    /// <para>RED if the refresh only covers the agreement side. The two caches are symmetric and a
    /// job that healed one of them would leave the same defect on the other — which is exactly the
    /// shape of bug this sprint's reviews kept finding.</para>
    /// </summary>
    [Fact]
    public async Task Refresh_ScheduledProfileCategoryChangeTakesEffectToday_FlipsTheCategoryCache()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("b2_cat");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        var originalCategory = await ScalarStringAsync(
            "SELECT employment_category FROM users WHERE user_id = @p0", employeeId);
        var scheduledCategory = originalCategory == "TIMELOENNET" ? "STANDARD" : "TIMELOENNET";

        await CloseProfileRowAsync(employeeId, F);
        await SeedProfileRowAsync(employeeId, F, null, employmentCategory: scheduledCategory);

        await RunBoundaryRefreshAsync(host);

        Assert.Equal(scheduledCategory, await ScalarStringAsync(
            "SELECT employment_category FROM users WHERE user_id = @p0", employeeId));
    }

    /// <summary>
    /// Idempotence: a second pass on the same day writes nothing. The token must move once per real
    /// change, not once per poll — this runs every five minutes, so a sweep that bumped
    /// unconditionally would invalidate every open HR drawer in the institution twelve times an hour
    /// and fill <c>users_audit</c> with rows describing nothing.
    ///
    /// <para>RED if the write is not guarded by the post-lock re-read comparison.</para>
    /// </summary>
    [Fact]
    public async Task Refresh_SecondPassOnTheSameDay_WritesNothing()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("b2_idem");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA, agreementCode: "AC");
        await CloseAgreementCodeRowAsync(employeeId, F);
        await SeedAgreementCodeRowAsync(employeeId, F, null, code: "HK");

        await RunBoundaryRefreshAsync(host);
        var versionAfterFirst = await ScalarLongAsync("SELECT version FROM users WHERE user_id = @p0", employeeId);
        var auditAfterFirst = await CountAsync("SELECT COUNT(*) FROM users_audit WHERE user_id = @p0", employeeId);

        await RunBoundaryRefreshAsync(host);

        Assert.Equal(versionAfterFirst, await ScalarLongAsync("SELECT version FROM users WHERE user_id = @p0", employeeId));
        Assert.Equal(auditAfterFirst, await CountAsync("SELECT COUNT(*) FROM users_audit WHERE user_id = @p0", employeeId));
    }

    /// <summary>
    /// The B8 shape seen from B2's side: when NO row covers today, the cached value is KEPT, not
    /// nulled, and no token moves. Both interactive writers use <c>COALESCE(today's value, cached
    /// value)</c> for exactly this reason; the columns are NOT NULL, so a job that did not obey the
    /// same rule would fail the write outright — or, worse, blank a value the writers deliberately
    /// preserve.
    ///
    /// <para>RED if the sweep's <c>IS NOT NULL</c> guards were dropped.</para>
    /// </summary>
    [Fact]
    public async Task Refresh_NoProfileRowCoversToday_KeepsTheCachedValue_AndDoesNotBumpTheToken()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("b2_hole");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        var cachedCategory = await ScalarStringAsync(
            "SELECT employment_category FROM users WHERE user_id = @p0", employeeId);

        // The only profile row now starts in twenty days: nothing covers today.
        await CloseProfileRowAsync(employeeId, F.AddDays(-5));
        await SeedProfileRowAsync(employeeId, F.AddDays(20), null);

        var versionBefore = await ScalarLongAsync("SELECT version FROM users WHERE user_id = @p0", employeeId);

        await RunBoundaryRefreshAsync(host);

        Assert.Equal(cachedCategory, await ScalarStringAsync(
            "SELECT employment_category FROM users WHERE user_id = @p0", employeeId));
        Assert.Equal(versionBefore, await ScalarLongAsync("SELECT version FROM users WHERE user_id = @p0", employeeId));
    }

    /// <summary>
    /// <b>★ The clock pin for B2.</b> At 23:30 UTC on F it is already F+1 in Copenhagen. A change
    /// scheduled for F+1 must NOT be applied yet, because every writer in this system stamps the UTC
    /// day and the cache must flip at the midnight the WRITER would have chosen.
    ///
    /// <para>RED if the sweep read <c>CopenhagenBusinessDate</c> (or any local-time day): it would
    /// flip the cache to the scheduled code an hour early, every night. This is the only fixture
    /// construction that can catch that — the ordinary <c>WithFixedToday</c> anchor pins UTC
    /// midnight, where both calendars agree and the assertion would pass under either clock.</para>
    /// </summary>
    [Fact]
    public async Task Refresh_UsesTheWritersUtcDay_NotTheCopenhagenBusinessDay()
    {
        using var host = HostAtInstant(FLateUtcEvening);
        using var client = host.CreateClient();

        var employeeId = NextId("b2_clock");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA, agreementCode: "AC");
        // Scheduled for TOMORROW in UTC terms — which is TODAY on the Copenhagen calendar at 23:30.
        await CloseAgreementCodeRowAsync(employeeId, F.AddDays(1));
        await SeedAgreementCodeRowAsync(employeeId, F.AddDays(1), null, code: "HK");

        var versionBefore = await ScalarLongAsync("SELECT version FROM users WHERE user_id = @p0", employeeId);

        await RunBoundaryRefreshAsync(host);

        Assert.Equal("AC", await ScalarStringAsync("SELECT agreement_code FROM users WHERE user_id = @p0", employeeId));
        Assert.Equal(versionBefore, await ScalarLongAsync("SELECT version FROM users WHERE user_id = @p0", employeeId));
    }

    // ════════════════════════════════════════════════════════════════════════
    // B8 — the "no record covers today" detector (HRP-015, widened)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>★ The headline B8 fact.</b> An employee whose only employment-profile row starts in the
    /// future appears on the cannot-register list, says that the EMPLOYMENT PROFILE is what is
    /// missing, and says when a scheduled record will cover them again.
    ///
    /// <para>RED before this task in the strongest possible way: the pre-S141 statement INNER JOINed
    /// a profile row covering today, so this employee — the one the list exists to find — was
    /// filtered out of it, and looked exactly like an employee with no problem at all.</para>
    ///
    /// <para><c>coveredFrom</c> is the part that makes the row actionable rather than alarming: a
    /// non-null value says "somebody scheduled a change and left today uncovered", which is a
    /// different conversation from "this employee's records are broken".</para>
    /// </summary>
    [Fact]
    public async Task CannotRegister_ProfileHoleWithAScheduledRow_IsListed_NamesTheProfile_AndSaysWhenCoverageResumes()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("b8_prof");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await CloseProfileRowAsync(employeeId, F.AddDays(-5));
        await SeedProfileRowAsync(employeeId, F.AddDays(20), null);

        var item = await SingleCannotRegisterItemAsync(host, employeeId);
        Assert.Equal("EMPLOYMENT_PROFILE", item.GetProperty("missingRecord").GetString());
        Assert.Equal(F.AddDays(-5), ReadDate(item.GetProperty("gapSince")));
        Assert.Equal(5, item.GetProperty("daysSinceGapStart").GetInt32());
        Assert.Equal(F.AddDays(20), ReadDate(item.GetProperty("coveredFrom")));
    }

    /// <summary>
    /// The pre-existing population still behaves, and now says which record is missing. The
    /// agreement hole was the ONLY thing this list detected before S141; widening it must not have
    /// cost that.
    ///
    /// <para>RED if the widened predicate lost the agreement arm, or if <c>missingRecord</c> were
    /// hard-coded rather than derived.</para>
    /// </summary>
    [Fact]
    public async Task CannotRegister_AgreementHole_StillListed_AndNamesTheAgreementCode()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("b8_agr");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await CloseAgreementCodeRowAsync(employeeId, F.AddDays(-3));

        var item = await SingleCannotRegisterItemAsync(host, employeeId);
        Assert.Equal("AGREEMENT_CODE", item.GetProperty("missingRecord").GetString());
        Assert.Equal(F.AddDays(-3), ReadDate(item.GetProperty("gapSince")));
        // Nothing is scheduled to close this one: it will not heal itself.
        Assert.Equal(JsonValueKind.Null, item.GetProperty("coveredFrom").ValueKind);
    }

    /// <summary>
    /// Both records missing is reported as <c>BOTH</c>, ONCE, with the EARLIER hole as the age
    /// anchor — the honest answer to "since when has this employee been unable to register".
    ///
    /// <para>RED if the two arms produced two rows for one employee (the list would double-count the
    /// worst-affected employees), or if the anchor took the later hole and understated the age.</para>
    /// </summary>
    [Fact]
    public async Task CannotRegister_BothRecordsMissing_ReportedOnce_AsBoth_WithTheEarlierAnchor()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("b8_both");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await CloseProfileRowAsync(employeeId, F.AddDays(-30));
        await CloseAgreementCodeRowAsync(employeeId, F.AddDays(-10));

        var item = await SingleCannotRegisterItemAsync(host, employeeId);
        Assert.Equal("BOTH", item.GetProperty("missingRecord").GetString());
        Assert.Equal(F.AddDays(-30), ReadDate(item.GetProperty("gapSince")));
    }

    /// <summary>
    /// A retired scheduled row — the zero-width close the soft-delete leaves behind (owner ruling
    /// OQ-5 (a)) — covers no day, so it must not be reported as coverage that is about to resume.
    ///
    /// <para>RED if the "next scheduled row" lookup did not exclude zero-width rows: HR would be
    /// told to wait for a record that somebody has already cancelled.</para>
    /// </summary>
    [Fact]
    public async Task CannotRegister_RetiredZeroWidthScheduledRow_IsNotReportedAsResumingCoverage()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("b8_zero");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await CloseProfileRowAsync(employeeId, F.AddDays(-5));
        // A scheduled row that was subsequently retired: effective_to == effective_from.
        await SeedProfileRowAsync(employeeId, F.AddDays(20), F.AddDays(20));
        // …and a real open row far enough out that the timeline stays legal (one open row only).
        await SeedProfileRowAsync(employeeId, F.AddDays(40), null);

        var item = await SingleCannotRegisterItemAsync(host, employeeId);
        Assert.Equal("EMPLOYMENT_PROFILE", item.GetProperty("missingRecord").GetString());
        Assert.Equal(F.AddDays(40), ReadDate(item.GetProperty("coveredFrom")));
    }

    /// <summary>
    /// <b>★ The clock pin for B8 — the owner's 2026-09-14 ruling, made falsifiable.</b> At 23:30 UTC
    /// on F it is already F+1 in Copenhagen. An agreement-code row that stops covering on F+1 still
    /// covers the employee on the UTC day, so they must NOT be listed.
    ///
    /// <para>RED if the endpoint or the read used <c>CopenhagenBusinessDate</c> like its four
    /// siblings in the same file: this employee would be reported as unable to register for the hour
    /// or two between Copenhagen midnight and UTC midnight, EVERY night, and a diagnostic list that
    /// cries wolf nightly is a list people stop reading. This fact is the reason the exception is
    /// safe to keep: somebody who "tidies" the clock back to match the neighbours breaks it.</para>
    /// </summary>
    [Fact]
    public async Task CannotRegister_UsesTheWritersUtcDay_NotTheCopenhagenBusinessDay()
    {
        using var host = HostAtInstant(FLateUtcEvening);
        using var client = host.CreateClient();

        var employeeId = NextId("b8_clock");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        // End-exclusive (ADR-018 D9): effective_to = F+1 means the row covers F and stops on F+1.
        await CloseAgreementCodeRowAsync(employeeId, F.AddDays(1));

        using var doc = JsonDocument.Parse(
            await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/cannot-register"));
        Assert.Equal(F, ReadDate(doc.RootElement.GetProperty("today")));
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
    }

    // ════════════════════════════════════════════════════════════════════════
    // B8, second half — the fail-closed reader's CAUGHT, NAMED condition
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The compliance read for an employee with no employment record covering the month answers a
    /// NAMED failure instead of an anonymous unhandled 500.
    ///
    /// <para><b>What changed and why it matters.</b> The refusal itself is correct and stays: a
    /// compliance verdict built on a guessed profile would be worse than no verdict (ADR-023 D3
    /// fail-closed). But until S141 this could only happen through a seeding defect, so an
    /// unnamed 500 was tolerable; now the product itself can create the state, so an operator has to
    /// be able to tell a broken server from an employee whose records have a hole.</para>
    ///
    /// <para>RED if the condition were left to escape as an unhandled exception — the response would
    /// carry no body at all and the <c>error</c> property would be absent. Also RED if the endpoint
    /// were "fixed" the wrong way, by defaulting the profile and returning 200: that is the pre-S33
    /// regression the sibling lifecycle suite also locks against.</para>
    ///
    /// <para>ADR-040 D7 is asserted too: the named body must carry no employment date and no as-of
    /// date, only a stable error code and a human-readable remedy.</para>
    /// </summary>
    [Fact]
    public async Task Compliance_NoEmploymentRecordCoversTheMonth_Returns500WithTheNamedCondition_AndNoDates()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("b8_comp");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        // Employed across the whole queried month, but with no profile row covering it: the window
        // check passes (so the endpoint does NOT take its "nothing to check" 200 exit) and the dated
        // resolver then finds nothing.
        await SetEmploymentStartDateAsync(employeeId, new DateOnly(F.Year - 1, 1, 1));
        await CloseProfileRowAsync(employeeId, new DateOnly(F.Year, 1, 1));
        await SeedProfileRowAsync(employeeId, F.AddYears(5), null);

        var rsp = await Client(host, GlobalAdminToken())
            .GetAsync($"/api/compliance/{employeeId}/period?year={F.Year}&month={F.Month}");

        Assert.Equal(HttpStatusCode.InternalServerError, rsp.StatusCode);
        using var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync());
        Assert.Equal("employment_record_gap", doc.RootElement.GetProperty("error").GetString());
        Assert.True(doc.RootElement.TryGetProperty("reason", out var reason));
        var reasonText = reason.GetString()!;
        Assert.DoesNotContain(F.Year.ToString(), reasonText, StringComparison.Ordinal);
        Assert.DoesNotContain("-01-01", reasonText, StringComparison.Ordinal);
    }

    // ─────────────────────────────── host + HTTP helpers ───────────────────────────────

    /// <summary>
    /// A host whose injected <see cref="TimeProvider"/> is pinned to an exact INSTANT rather than to
    /// a bare calendar date. <c>StatsTidWebApplicationFactory.WithFixedToday</c> takes a
    /// <c>DateOnly</c> and pins UTC midnight — deliberately, because that is what almost every fact
    /// wants — but UTC midnight is precisely the instant at which the UTC day and the Copenhagen
    /// business day AGREE, so it cannot express the one distinction the two clock facts exist to
    /// make. Same registration mechanism, one constructor over.
    /// </summary>
    private WebApplicationFactory<Program> HostAtInstant(DateTimeOffset instant)
        => _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(instant))));

    /// <summary>
    /// Grabs the host's OWN running <see cref="DelegationExpiryService"/> — registered only as
    /// <see cref="IHostedService"/>, so it cannot be resolved as itself — and runs a deterministic
    /// single-shot boundary-refresh pass instead of waiting on its five-minute timer. The SAME
    /// singleton the host started, so no second sweeper competes against this container.
    /// </summary>
    private static async Task RunBoundaryRefreshAsync(WebApplicationFactory<Program> host)
    {
        var sweeper = host.Services.GetServices<IHostedService>().OfType<DelegationExpiryService>().Single();
        await sweeper.RefreshEffectiveDateBoundariesAsync(CancellationToken.None);
    }

    private async Task<JsonElement> SingleCannotRegisterItemAsync(
        WebApplicationFactory<Program> host, string employeeId)
    {
        var json = await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/cannot-register");
        // Cloned, because the JsonDocument backing it is disposed when this helper returns.
        using var doc = JsonDocument.Parse(json);
        var matches = doc.RootElement.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("employeeId").GetString() == employeeId)
            .ToList();
        return Assert.Single(matches).Clone();
    }

    private static HttpClient Client(WebApplicationFactory<Program> host, string token)
    {
        var c = host.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    private static string HrToken(string orgId) => NewTokenService().GenerateToken(
        employeeId: "hr_s141_edb_actor", name: "hr_s141_edb_actor", role: StatsTidRoles.LocalHR, agreementCode: "AC",
        orgId: orgId, scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });

    private static string GlobalAdminToken() => NewTokenService().GenerateToken(
        employeeId: "hr_s141_edb_admin", name: "hr_s141_edb_admin", role: StatsTidRoles.GlobalAdmin, agreementCode: "AC",
        scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    private static DateOnly ReadDate(JsonElement e) => DateOnly.Parse(e.GetString()!);

    // ─────────────────────────────── seed helpers ───────────────────────────────

    private Task SetEmploymentStartDateAsync(string employeeId, DateOnly start) =>
        ExecAsync("UPDATE users SET employment_start_date = @p1 WHERE user_id = @p0", employeeId, start);

    private Task CloseAgreementCodeRowAsync(string employeeId, DateOnly effectiveTo) =>
        ExecAsync(
            "UPDATE user_agreement_codes SET effective_to = @p1 WHERE user_id = @p0 AND effective_to IS NULL",
            employeeId, effectiveTo);

    private Task SeedAgreementCodeRowAsync(
        string employeeId, DateOnly effectiveFrom, DateOnly? effectiveTo, string code = "AC") =>
        ExecAsync(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @p0, @p3, @p1, @p2, 1)
            """, employeeId, effectiveFrom, (object?)effectiveTo ?? DBNull.Value, code);

    private Task CloseProfileRowAsync(string employeeId, DateOnly effectiveTo) =>
        ExecAsync(
            "UPDATE employee_profiles SET effective_to = @p1 WHERE employee_id = @p0 AND effective_to IS NULL",
            employeeId, effectiveTo);

    /// <summary>Inserts a dated profile row. <paramref name="employmentCategory"/> defaults to the
    /// employee's current cached category, mirroring <c>RegressionSeed</c>.</summary>
    private Task SeedProfileRowAsync(
        string employeeId, DateOnly effectiveFrom, DateOnly? effectiveTo, string? employmentCategory = null) =>
        ExecAsync(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version, employment_category)
            VALUES (
                gen_random_uuid(), @p0, 1.000, NULL, @p1, @p2,
                (SELECT COALESCE(MAX(version), 0) + 1 FROM employee_profiles WHERE employee_id = @p0),
                COALESCE(@p3, (SELECT u.employment_category FROM users u WHERE u.user_id = @p0)))
            """, employeeId, effectiveFrom, (object?)effectiveTo ?? DBNull.Value,
            (object?)employmentCategory ?? DBNull.Value);

    // ─────────────────────────────── raw DB helpers ───────────────────────────────

    private async Task<int> CountAsync(string sql, params object[] args)
        => Convert.ToInt32(await ScalarAsync(sql, args));

    private async Task<long> ScalarLongAsync(string sql, params object[] args)
        => Convert.ToInt64(await ScalarAsync(sql, args));

    private async Task<string> ScalarStringAsync(string sql, params object[] args)
        => (string)(await ScalarAsync(sql, args))!;

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100 // compile-time test constant; values bound via parameters below
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return await cmd.ExecuteScalarAsync();
    }

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100 // compile-time test constant; values bound via parameters below
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }
}
